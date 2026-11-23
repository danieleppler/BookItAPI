# Decisions

A log of design decisions for the BookIt API, with the reasoning behind each.

---

## 1. Registered users are always assigned the `Client` role

**Date:** 2026-11-22

**Decision:** `POST /api/auth/register` always creates the user with `Role = Client`. The request does not accept a role. Elevating a user to `BusinessOwner` is done through a separate flow (e.g. admin approval), which is out of scope for this project.

**Why:** If the client could choose its role at registration, anyone could self-assign `BusinessOwner` and bypass whatever approval the elevation flow enforces (privilege escalation / mass assignment).

**Consequences:**
- `RegisterRequest` has no `Role` field; a `role` sent in the body is ignored.
- Business owners must currently be created via seed data or directly in the database until the elevation flow exists.

---

## 2. Business listing: page-number pagination and case-insensitive, wildcard-safe filtering

**Date:** 2026-11-22

**Decision — pagination:** `GET /api/businesses?page=1&pageSize=20` uses page-number (offset) pagination with a response envelope:

```json
{ "items": [...], "page": 1, "pageSize": 20, "totalCount": 20, "totalPages": 1 }
```

- `page` defaults to 1 (max 100,000, so the offset can't overflow `int`); `pageSize` defaults to 20 (max 100).
- Results are ordered by `Name`, then `Id` as a tie-breaker, so pages are stable.
- `totalCount` is the count of businesses matching the filters, not just the current page.
- A page past the end returns an empty `items` list (200), not an error.

**Why page-number + envelope:**
- The spec requires the total count of matching businesses, which pairs naturally with page-number pagination.
- Simplest for clients: jump to any page, render "page X of Y".
- Everything in the body is easy to read and test in Postman (vs. metadata in headers like `X-Total-Count` / `Link`).

**Alternatives considered:**
- **Cursor / keyset** (`?after=<cursor>`): constant speed at any depth and stable under concurrent inserts, but no jumping to arbitrary pages and the total count needs a separate query. Better suited to large or fast-changing feeds; overkill for ~20 businesses.
- **Offset + limit** (`?offset=40&limit=20`): same mechanism, less natural for page-based UIs.

**Decision — filtering:**
- `category` is an **exact, case-insensitive** match (`?category=barber` matches `Barber`).
- `search` is a **contains, case-insensitive** match on the business name (`?search=studio 1` matches `Barber Studio 13`).
- Both are implemented with Postgres `ILIKE` via `EF.Functions.ILike`, and user input has LIKE wildcards (`%`, `_`, `\`) escaped so it is matched literally.
- Both filters are optional and combine with AND.

**Why this filtering:**
- Case-insensitive matching is what users expect from a search box and a category filter; a case-sensitive `==` would return nothing for `barber`.
- Without escaping, `?search=%` would match every business (wildcard injection). SQL injection itself is already prevented because EF Core sends the values as parameters, never as SQL text.

**Trade-offs:**
- Count and page are two separate queries; a business inserted between them can make `totalCount` off by one for that request. Acceptable for a listing.
- Offset pagination gets slower on deep pages (the DB still reads skipped rows). Irrelevant at this size.
- `ILIKE` can't use a regular B-tree index, so these filters scan the table. Fine for ~20 businesses.
- At scale: store categories in a normalized case (or use `citext`) and compare with `==` so the `Category` index is used; for name search, add a `pg_trgm` trigram index, which supports `ILIKE '%...%'`.

---

## 3. Booking: new appointments are `Pending`, and the database prevents overlapping bookings

**Date:** 2026-11-23

**Decision:**
- `POST /api/appointments` is allowed for the `Client` role only (`BusinessOwner` → 403). The client id is taken from the JWT, never from the request body.
- A new appointment is always created with status **`Pending`** (awaiting the owner's confirmation). `Pending` and `Confirmed` both **reserve the slot**; only `Cancelled` frees it.
- Two non-cancelled appointments of the **same service** may not overlap in time. Ranges are `[start, end)`, so back-to-back bookings (10:00–11:00, then 11:00–12:00) are allowed.
- This rule is enforced by a **Postgres exclusion constraint**, not by application code:
  ```sql
  EXCLUDE USING gist ("ServiceId" WITH =, tstzrange("StartTime", "EndTime", '[)') WITH &&)
  WHERE ("Status" <> 'Cancelled')
  ```
  The API inserts and, if Postgres rejects it with `23P01` (exclusion violation), returns 409.
- `Appointment` gets a stored `EndTime` column (`StartTime + service duration`), required because the constraint must be expressed over the row's own columns. A check constraint enforces `EndTime > StartTime`.
- Start time must be in the future and at most one year ahead (400 otherwise). The upper bound also blocks overflow from inputs like `9999-12-31`.

**Why:**
- A "check for overlap, then insert" in application code has a race: two concurrent requests can both see the slot as free and both insert. The exclusion constraint is checked atomically by the database, so it holds under concurrency and for every write path (API, scripts, manual SQL). Verified: 10 concurrent requests for the same slot → one 201, nine 409.

**Alternatives considered:**
- **Row lock** (`SELECT ... FOR UPDATE` on the service, then check + insert in a transaction): correct, no schema change, but only protects code paths that remember to take the lock.
- **Serializable isolation:** Postgres aborts one of the conflicting transactions, which then needs retry logic.
- **Advisory lock per service:** same as the row lock, less self-explanatory.

**Consequences / trade-offs:**
- Requires the `btree_gist` extension and a hand-written SQL step in the `AddAppointmentEndTime` migration (EF can't model exclusion constraints).
- `EndTime` is fixed at booking time. If a service's duration later changes, existing appointments keep their original end time, which is the desired behavior for already-booked slots.
- The overlap rule is per service. A business that can't run two different services at once (e.g. a single barber) would need the rule per business instead; out of scope here.
- A client can still hold overlapping appointments at different services; not restricted.
- The seeder marks a randomly generated slot as `Cancelled` if it would overlap an earlier active one, so seed data satisfies the constraint.

---

## 4. Listing appointments: scoped by the JWT, overlap-based date filter, single projected query

**Date:** 2026-11-23

**Decision:**
- `GET /api/appointments?from=&to=&status=` is open to both roles. The scope comes **only from the token** (`sub` + role claim); no query parameter can widen it:
  - `Client` → `WHERE ClientId = @me`
  - `BusinessOwner` → `WHERE Service.Business.OwnerId = @me`
- The scope filter is applied first; `from` / `to` / `status` can only narrow it further.
- **Date range uses overlap semantics** on `[from, to)`: an appointment is returned if `EndTime > from AND StartTime < to`. So an appointment already in progress at `from` is included. Either bound may be omitted. `from >= to` → 400.
- `status` binds by name (case-insensitive); an unknown value → 400.
- Results are ordered by `StartTime`, then `Id`.
- The response is `AppointmentResponse` (same DTO as `POST`), with `serviceName` and `businessName` for display.

**Why — no N+1:** the query ends in `.Select(a => new AppointmentResponse(..., a.Service.Name, ..., a.Service.Business.Name, ...))`. EF translates the navigations into `INNER JOIN`s, so it is **one SQL round trip** no matter how many rows come back. No entities are materialized or tracked, and lazy loading is not enabled, so nothing can load related rows one by one. `Include` was not needed because a projection loads only the columns it uses.

**Why overlap rather than "start time in range":** a calendar view for "today" should show a 23:30–00:30 appointment that started yesterday. Filtering on `StartTime` alone would hide it.

**Trade-offs / follow-ups:**
- **No pagination yet.** The result is unbounded; fine for seed data, but an owner with many businesses could get a large response. The fix is the same envelope as `/api/businesses`, or a default/maximum date window.
- The owner path joins through `Services` → `Businesses`; the FK indexes on `Services.BusinessId` and `Businesses.OwnerId` (added by EF convention) cover it. The client path uses `IX_Appointments_ClientId`.
- Times with a `+hh:mm` offset must be URL-encoded (`%2B`) in the query string, or use `Z`.

---

## 5. Business summary: owner-only, aggregated in the database, one row per service

**Date:** 2026-11-23

**Decision:**
- `GET /api/businesses/{id}/summary` requires the `BusinessOwner` role (`[Authorize(Roles = ...)]`, so a Client gets 403 from the framework). The service then checks `Business.OwnerId` against the JWT `sub`: another owner gets **403**, and a business that doesn't exist gets **404**.
- "Upcoming" = `StartTime > now` **and** `Status != Cancelled`, so both `Pending` and `Confirmed` count, as the spec says. `Pending` still reserves the slot (see decision 3), so it counts as expected revenue.
- Response: one entry per service (`upcomingAppointments`, `expectedRevenue = count × price`) plus business-level totals.
- **One database round trip:** the ownership check and the aggregation come back in the same query.

**Why the aggregation is written this way:**
```csharp
_db.Businesses.Where(b => b.Id == id)
    .Select(b => new { b.Name, b.OwnerId,
        Services = b.Services.Select(s => new { s.Id, s.Name, s.Price,
            UpcomingCount = s.Appointments.Count(a => a.StartTime > now && a.Status != Cancelled) }).ToList() })
    .SingleOrDefaultAsync();
```
- EF translates this into one SQL statement: the business row `LEFT JOIN`ed to its services, with the `Count(...)` as a correlated `COUNT(*)` subquery per service. The counting happens in Postgres and only one row per service comes back, not every appointment.
- Ownership is checked in C# on the returned `OwnerId`. An earlier version used a separate ownership query first; merging them saves a round trip on every request, which matters more than the cheap counts wasted on the rare 403.
- Starting from `Services` (not `GroupBy` on `Appointments`) means a service with **no** upcoming bookings still appears with `0`. Grouping appointments would silently drop it.
- `count × price` and the totals are computed in C# on those few aggregated rows, using `decimal`, so money stays exact.
- The `(ServiceId, StartTime)` index serves the subquery.

**Alternatives considered:**
- `GroupBy(a => a.ServiceId)` on appointments, then joining services: more code and drops zero-booking services unless you do a left join.
- `Sum(a => a.Service.Price)` in SQL: same result, but "count × price" is the stated definition and keeps the count and revenue visibly consistent.

**Trade-offs:**
- Returning 404 for a missing business but 403 for someone else's business tells an owner which ids exist. Business ids aren't secret (`GET /api/businesses` lists them all), so that's acceptable.
- Revenue uses the service's **current** price. If prices change, already-booked appointments would need a stored `PriceAtBooking` to be exact; out of scope.

---

## 6. Middlewares: request logging with correlation ID, and global exception handling

**Date:** 2026-11-23

**Decision:** two custom convention-based middleware classes in `Middleware/`, registered first in the pipeline in this order:

```csharp
app.UseMiddleware<RequestLoggingMiddleware>();     // outermost
app.UseMiddleware<ExceptionHandlingMiddleware>();
```

**Order:** request logging wraps the exception handler, so when a request crashes, the handler first turns it into a 500 and the logger then records that real status and the full duration. Both sit before authentication, so 401/403 responses are logged and get a correlation ID too.

**`RequestLoggingMiddleware`:**
- Logs `HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms [CorrelationId: ...]` once per request, in a `finally`, as structured log fields.
- Logs the **path only, not the query string**, so user-supplied filter values don't end up in logs.
- **Correlation ID:** reuses the caller's `X-Correlation-ID` header if it matches `^[A-Za-z0-9._-]{1,64}$`; otherwise it generates a GUID. Reusing it lets one request be traced across services. The allow-list stops a caller from injecting newlines (forged log lines) or huge values.
- The id is stored in `HttpContext.TraceIdentifier` (the framework's per-request id), opened as a log scope, and added to the response with `Response.OnStarting(...)`. `OnStarting` callbacks survive `Response.Clear()`, so the header is present even on the 500 written by the exception handler.

**`ExceptionHandlingMiddleware`:**
- Catches any unhandled exception, logs it **in full** (`LogError(ex, ...)` with the correlation ID), and returns a generic `application/problem+json` 500: a fixed title and detail, the path, and the `correlationId`. No exception message, type or stack trace reaches the client, in any environment.
- A client disconnect (`OperationCanceledException` while `RequestAborted` is set) is logged at Information and gets no response, since nobody is listening and it isn't a server error.
- If the response has already started, the status can't be changed any more, so it logs and rethrows and the server aborts the connection.

**Why custom classes instead of `UseExceptionHandler` / `UseHttpLogging`:** the spec asks for hand-written middleware. The built-ins would also work; `UseHttpLogging` doesn't produce correlation IDs on its own.

**Verified:** 200 / 401 responses carry the header; a caller-supplied safe id is echoed back, and an unsafe one is replaced. A real failure (the test instance's DB connections terminated mid-run) returned a clean 500 body with `correlationId`, the full Npgsql stack trace appeared only in the server log, and the request log line showed `responded 500`.

**Trade-offs:**
- The console log formatter doesn't print scopes by default, so the correlation ID is also written into the two middleware log messages explicitly. To get it on *every* log line (EF, services), turn on `IncludeScopes` or use a structured logging sink.
- Expected errors (404, 409, ...) are still returned by the controllers as ProblemDetails; this middleware only covers what nothing else handled.
