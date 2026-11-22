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
