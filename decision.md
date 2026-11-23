# Decisions

Design decisions for the BookIt API, with the reasoning behind each.

---

## 1. Registered users are always `Client`

**Decision:** `POST /api/auth/register` always creates a `Client`. The request has no role field, and a `role` sent in the body is ignored. Becoming a `BusinessOwner` needs a separate approval flow, which is out of scope.

**Why:** If callers could pick their role, anyone could make themselves `BusinessOwner` (privilege escalation). For now, owners come from seed data.

---

## 2. Business listing: page-number pagination, case-insensitive filtering

**Pagination:** `GET /api/businesses?page=1&pageSize=20` returns `{ items, page, pageSize, totalCount, totalPages }`.
- `page` defaults to 1 (max 100,000). `pageSize` defaults to 20 (max 100).
- Results are ordered by `Name`, then `Id`, so pages are stable. A page past the end returns an empty `items` list.
- **Why:** the spec requires a total count, which fits page numbers, and it's simple for clients. Cursor pagination is faster at depth but overkill for this data size.

**Filtering:**
- `category` is an exact match and `search` is a "name contains" match. Both are case-insensitive (`ILIKE`) and are combined with AND.
- `%`, `_` and `\` in user input are escaped, so `?search=%` doesn't match everything. EF parameters already prevent SQL injection.

**Trade-offs:** count and page are separate queries, so `totalCount` can be off by one during concurrent inserts. `OFFSET` and `ILIKE` scan rows, which is fine at this size. At scale, use `citext` / normalized categories and a `pg_trgm` index for name search.

---

## 3. Booking: stored `EndTime`, and double-booking prevented by Postgres itself

**Decision:**
- `Appointment` has a stored `EndTime` column (`StartTime` + the service's duration), set when the appointment is booked. A check constraint enforces `EndTime > StartTime`.
- Two non-cancelled appointments of the same service can't overlap. Postgres enforces this with an exclusion constraint, which needs the `btree_gist` extension:
  ```sql
  EXCLUDE USING gist ("ServiceId" WITH =, tstzrange("StartTime", "EndTime", '[)') WITH &&)
  WHERE ("Status" <> 'Cancelled')
  ```
  With `[)` ranges, back-to-back slots (10:00–11:00, then 11:00–12:00) are allowed. `Pending` and `Confirmed` both hold the slot.
- The API simply inserts. If Postgres rejects the row with an exclusion violation (`23P01`), the API returns 409.

**Why:**
- Checking for an overlap in code and then inserting has a race: two concurrent requests can both see the slot as free and both insert. The database checks the constraint atomically, so the rule holds under concurrency and for every write path (API, scripts, manual SQL).
- `EndTime` has to be stored because the constraint can only use the row's own columns.
- **Verified:** 10 concurrent requests for the same slot returned one 201 and nine 409s.

**Alternatives:** a row lock (`SELECT … FOR UPDATE` on the service) only protects code paths that remember to take it. Serializable isolation works too, but the losing transaction needs retry logic.

**Trade-offs:**
- EF Core can't model exclusion constraints, so the constraint is raw SQL in the `AddAppointmentEndTime` migration.
- `EndTime` is fixed at booking time, so changing a service's duration later doesn't move existing bookings. That's intended.
- The rule is per service. A single barber who can't run two services at once would need it per business instead.

---

## Bonus features and future work

**Bonus features implemented:**
- **Login rate limiting:** a sliding window allows 5 attempts per minute per IP. Further attempts get 429 with `Retry-After`.
- **Refresh tokens:** 15-minute access tokens and 7-day refresh tokens. Refresh tokens are rotated on every use, stored only as SHA-256 hashes, and reuse revokes the whole token family.

**With more time:**
- **Caching:** an output cache for `GET /api/businesses` (30 s, varying by query). Appointments would only be cached after pagination is added, since that data is per user and changes on every booking.
- **Register rate limit:** each registration runs bcrypt, which is CPU-heavy, so a limit would stop spam signups from exhausting the CPU.
- **Refresh-token cleanup:** an hourly background job that deletes expired tokens in batches, so the table doesn't grow forever.
