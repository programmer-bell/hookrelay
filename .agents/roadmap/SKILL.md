---
name: roadmap
description: Phased, checkbox-driven build plan for HookRelay. Use this to determine what to build next, in what order, and what "done" means for each phase. Always read alongside the Instructions skill.
---

# HookRelay: Roadmap

Read `/.agents/instructions/SKILL.md` first. Build **one phase at a time**. Do not start a phase until the previous phase's Definition of Done (DoD) is fully checked. Tick boxes (`- [x]`) in this file as you complete tasks. After finishing a phase, **stop and summarize** for the user.

All .NET commands run via `docker compose run --rm sdk dotnet ...` (see Instructions §0).

---

## Phase 0: Scaffold & Tooling
**Goal:** An empty but runnable app inside Docker.

- [x] Create solution structure per README (`src/HookRelay`, `tests/HookRelay.Tests`, `HookRelay.sln`)
- [x] `HookRelay.csproj`: net8.0, `Nullable` enabled, `ImplicitUsings` enabled, packages `Npgsql` + `Dapper` only
- [x] `tests/HookRelay.Tests` (xUnit test project)
- [x] Minimal `Program.cs` with `/healthz` returning `200 OK`
- [x] `appsettings.json` with `Delivery` section (MaxAttempts 6, TimeoutSeconds 10, PollIntervalSeconds 2)
- [x] Add `.devcontainer/devcontainer.json` (mcr.microsoft.com/dotnet/sdk:8.0, extensions: C# Dev Kit, Docker)
- [x] Vendor `htmx.min.js` into `wwwroot/js/` (download in the Dockerfile-free way: `curl` from unpkg into the file once, then commit it) plus add `wwwroot/css/site.css`
- [x] `.env.example` with `DATABASE_URL` placeholder
- [x] `global.json` (SDK 8.0.100, rollForward latestFeature), `Directory.Build.props` (Nullable, ImplicitUsings, analyzers), `.editorconfig` (4 spaces, LF, file-scoped namespaces, System usings first)
- [x] Fix GitHub Actions `ci.yml` (global.json-driven restore/format/build/test)
- [x] Dockerfile restore paths, docker-compose `sdk` service, and devcontainer verified

**DoD:** `docker compose up --build` serves `/healthz` = 200 at `localhost:8080`. `docker compose run --rm sdk dotnet test` runs (0 tests OK). (verified 2026-09-21)

---

## Phase 1: Data Layer & Migrations
**Goal:** Postgres connectivity and a migration system.

- [x] `AppOptions` (`Delivery` section: MaxAttempts, TimeoutSeconds, PollIntervalSeconds) bound from config
- [x] `Db` wrapper: parse `DATABASE_URL` (URI and key=value), SSL rule, startup connection retry (5 × 2s)
- [x] `Migrator`: `schema_migrations` table, apply ordered `.sql` files transactionally, embedded as content files copied to output
- [x] `001_init.sql`: tables `endpoints`, `captured_requests`, `deliveries`, `delivery_attempts`, with indexes:
  - unique `endpoints(slug)`
  - `deliveries(status, next_attempt_at)`
  - unique partial index on `captured_requests(endpoint_id, idempotency_key) WHERE idempotency_key IS NOT NULL`
  - `captured_requests(endpoint_id, received_at DESC)`
- [x] Domain records + `EndpointRepository` (create, get by slug, list, delete)
- [x] `/readyz` endpoint running `SELECT 1`
- [x] Add a local `postgres` service to docker-compose (already provided) and confirm migrations apply on boot

**DoD:** App boots against local Postgres, creates the schema, `/readyz` = 200. Restarting does not re-apply migrations. (verified 2026-09-21)

---

## Phase 2: Endpoint Management UI
**Goal:** Create/list/delete endpoints from the browser using htmx.

- [x] Layout with Bootstrap 5 (CDN) + htmx script
- [x] `GET /` dashboard listing endpoints (`_EndpointList` partial)
- [x] `POST /endpoints` (`hx-post`): fields `name`, `target_url`; server generates `slug` (10 chars, URL-safe, crypto-random) and `signing_secret` (32 bytes, hex)
- [x] Validate `target_url` (http/https only, **SSRF checks from Instructions §5**), return validation errors as a fragment with status 422
- [x] `DELETE /endpoints/{id}` (`hx-delete`, `hx-confirm`)
- [x] Unit tests for URL/SSRF validation

**DoD:** User can create and delete endpoints with no full-page reloads. Private/loopback targets are rejected. Tests pass.

---

## Phase 3: Ingest API
**Goal:** Capture webhooks reliably.

- [x] `ANY /h/{slug}`: read body (256 KB cap → `413`), headers, query, method
- [x] Idempotency-Key handling (duplicate returns `200` + original id)
- [x] **Single transaction:** insert `captured_requests` + `deliveries(status='pending', next_attempt_at=now())`
- [x] Built-in rate limiter (60/min per slug) with `429` + `Retry-After`
- [x] Security-headers middleware
- [x] Redact `Authorization` and `Cookie` header values before storing (store `[redacted]`)
- [x] `EventBus` (`Channel<T>`-based, multi-subscriber, bounded, drop-oldest) and publish a "request captured" event

**DoD:** `curl -X POST localhost:8080/h/{slug} -d '{"a":1}'` returns `202`, rows exist in both tables, duplicate idempotency key creates nothing, 61st request in a minute returns `429`.

---

## Phase 4: Inspector UI & Live Feed
**Goal:** See webhooks arrive in real time.

- [x] `GET /endpoints/{slug}` inspector page: endpoint info (ingest URL with copy button), recent 50 requests (`_RequestRow`)
- [x] Expandable row showing method, headers table, pretty-printed body (JSON detected, otherwise raw text)
- [x] `GET /endpoints/{slug}/stream` SSE endpoint fed by `EventBus`, with a 15s heartbeat comment
- [x] New rows prepend live via htmx SSE (or minimal `EventSource` fallback)
- [x] Delivery status badge partial (`_DeliveryBadge`), which reflects pending, succeeded, or dead

**DoD:** Sending a curl to the ingest URL makes a new row appear in an open browser tab within about 1 second without refresh.

---

## Phase 5: Delivery Worker
**Goal:** Reliable, signed forwarding with retries and DLQ.

- [x] `RetryPolicy` (pure): `NextDelay(attempt, random)` and `IsExhausted(attempt, max)`, plus unit tests for cap, bounds, and jitter range
- [x] `HmacSigner` (pure) plus unit tests using a known vector
- [x] `DeliveryRepository`: `ClaimDueAsync` (SKIP LOCKED pattern), `MarkSucceeded`, `MarkRetry`, `MarkDead`, `RecoverStuckAsync`, `RecordAttempt`
- [x] `DeliveryWorker : BackgroundService` per Instructions §5 (send-time SSRF re-check, timeouts, attempt recording, graceful shutdown)
- [x] Publish delivery status change events on `EventBus` so the inspector badge updates live
- [x] Attempt history shown in the expanded row (time, status code, duration, error)

**DoD:** With a target that returns 500, you can watch attempts 1 to 6 with growing delays, then status `dead`. With a healthy target, status becomes `succeeded` on the first attempt. Killing the container mid-delivery and restarting recovers the stuck job.

---

## Phase 6: Replay & Operability
**Goal:** Operator tools and robustness.

- [x] `POST /deliveries/{id}/replay` (`hx-post`) resets a `dead` delivery to `pending` with `attempt_count=0`, and returns the updated badge fragment
- [x] Filter tabs on the inspector: All / Pending / Succeeded / Dead (`hx-get` with `hx-target`)
- [x] Data retention: background cleanup deleting captured requests older than 7 days (runs hourly, in the worker or a second small `BackgroundService`)
- [x] Structured logging on key events (ingest, attempt, dead, replay)
- [x] Graceful-shutdown verification

**DoD:** A dead delivery can be replayed from the UI and succeeds once the target is fixed. Old data is purged. Logs are structured.

---

## Phase 7: Hardening, Tests, Deploy
**Goal:** Production-ready on Render free tier.

- [x] Review the Dockerfile for the non-root user and `PORT` binding, and verify the image builds cleanly
- [x] Add integration-style test for the ingest idempotency path (may use in-memory fakes; a live DB is optional)
- [ ] README: fill in live demo URL, verify every command in it
- [ ] Deploy: Neon DB, Render web service (Docker, Free), env vars, health check `/healthz`
- [ ] Smoke-test production: create endpoint, send webhook, watch delivery
- [x] Optional: add `render.yaml`

**DoD:** Public URL works end to end. README is accurate. All tests pass. No secrets in git history.

---

## Backlog (only if asked by the user)

- API key auth / multi-user
- Custom headers per endpoint
- Prometheus `/metrics`
- Inbound provider signature verification (Stripe/GitHub)