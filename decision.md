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

## 3. Middlewares: request logging with correlation ID, global exception handling

**Decision:** two custom middleware classes run first in the pipeline. `RequestLoggingMiddleware` is the outer one and `ExceptionHandlingMiddleware` sits inside it. Because the logger wraps the handler, it records the real 500 and the full duration. Both run before auth, so 401s and 403s are logged too.

**Request logging:**
- One structured line per request: method, path (no query string, to keep user input out of logs), status and elapsed ms.
- **Correlation ID:** the caller's `X-Correlation-ID` is reused if it matches `^[A-Za-z0-9._-]{1,64}$`, which blocks log injection. Otherwise a new GUID is generated.
- The ID is stored in `TraceIdentifier` and echoed in the response header through `OnStarting`, so it's also present on 500s.

**Exception handling:**
- Logs the full exception and returns a generic `application/problem+json` 500 that includes `correlationId`. No exception details reach the client.
- When the client disconnects, it logs at Information and sends no response. If the response has already started, it rethrows.

**Verified:** killing the test instance's DB connections returned a clean 500 with a correlation ID, and the stack trace appeared only in the server log.

**Trade-off:** the console logger doesn't print scopes by default, so the ID is written into the two middleware messages explicitly.

---

## Bonus features and future work

**Bonus features implemented:**
- **Login rate limiting:** a sliding window allows 5 attempts per minute per IP. Further attempts get 429 with `Retry-After`.
- **Refresh tokens:** 15-minute access tokens and 7-day refresh tokens. Refresh tokens are rotated on every use, stored only as SHA-256 hashes, and reuse revokes the whole token family.

**With more time:**
- **Caching:** an output cache for `GET /api/businesses` (30 s, varying by query). Appointments would only be cached after pagination is added, since that data is per user and changes on every booking.
- **Register rate limit:** each registration runs bcrypt, which is CPU-heavy, so a limit would stop spam signups from exhausting the CPU.
- **Refresh-token cleanup:** an hourly background job that deletes expired tokens in batches, so the table doesn't grow forever.
