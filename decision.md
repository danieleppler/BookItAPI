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
