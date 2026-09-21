---
name: instructions
description: Coding rules, architecture constraints, and conventions for building HookRelay (ASP.NET Core 8 + htmx + Postgres). Read this BEFORE writing or modifying any code in this repository.
---

# HookRelay: Engineering Instructions

You are building **HookRelay**, a webhook capture-and-relay service. Follow this file strictly. The companion file `/.agents/Roadmap/SKILL.md` defines WHAT to build and in WHICH order. This file defines HOW.

## 0. Environment Constraints (critical)

- **.NET is NOT installed on the host machine.** Never run `dotnet` directly on the host.
- Run every .NET command through Docker:
```bash
  docker compose run --rm sdk dotnet <args>
```
  If you are already inside the VS Code Dev Container, plain `dotnet <args>` is fine.
- Do not tell the user to install the .NET SDK, Node, or npm. There is no Node toolchain in this project.
- Shell is Ubuntu on WSL2. Use POSIX-style paths and commands.

## 1. Tech Stack (fixed, do not deviate)

- .NET 8, ASP.NET Core **Minimal APIs** + **Razor Pages** (used only for HTML views and partials)
- **htmx** (vendored file in `wwwroot/js/htmx.min.js`) + **Bootstrap 5** via CDN
- PostgreSQL via **Npgsql** + **Dapper**
- xUnit for tests

### Dependency policy (strict)
- Allowed NuGet packages in `HookRelay.csproj`: **`Npgsql`**, **`Dapper`** only.
- Test project may add: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing` (only if needed).
- **Forbidden:** EF Core, MediatR, AutoMapper, FluentValidation, Serilog, Polly, Hangfire, Redis clients, any message broker, any JS framework, npm/webpack/vite.
- Need something new? Implement it with the standard library, or ask the user. Do not add a package silently.

## 2. Architecture Rules

1. **Feature-folder layout** (see README). Endpoint mapping lives in `Features/*`, data access in `Data/*`, background/cross-cutting logic in `Services/*`.
2. `Program.cs` is only a composition root: DI registration, middleware, and calls to `MapXxxEndpoints()` extension methods. No business logic here.
3. **No ORM.** Use Dapper with parameterized SQL only. Never concatenate user input into SQL.
4. Repositories are thin classes that take `Db` (a wrapper over `NpgsqlDataSource`). Register them as singletons (they are stateless).
5. **Pure logic goes in pure classes.** `RetryPolicy` and `HmacSigner` must have no I/O and no static clock access (inject `TimeProvider` or accept `DateTimeOffset now`) so they are unit-testable.
6. All async methods accept and propagate `CancellationToken`.
7. Use `TimeProvider` (built into .NET 8), not `DateTime.UtcNow`, in new code.
8. Use `record` for DTOs and immutable domain snapshots; use `sealed` on classes not designed for inheritance.
9. Nullable reference types **enabled**; treat warnings as errors in Release.
10. One public type per file, file name equals type name.

## 3. Database Rules

- Migrations are plain `.sql` files in `src/HookRelay/Migrations/`, named `NNN_description.sql` (three-digit, ordered).
- `Migrator` applies unapplied files in order inside a transaction and records them in a `schema_migrations` table. It runs on startup.
- Never edit an applied migration. Add a new one.
- The queue MUST use this claim pattern:
```sql
  UPDATE deliveries SET status='delivering', updated_at=now()
  WHERE id IN (
    SELECT id FROM deliveries
    WHERE status='pending' AND next_attempt_at <= now()
    ORDER BY next_attempt_at
    LIMIT @batch
    FOR UPDATE SKIP LOCKED)
  RETURNING *;
```
- **Stuck job recovery:** deliveries in `delivering` for longer than 5 minutes must be reset to `pending` (crash recovery). Implement it in the worker loop.
- Ingest must write `captured_requests` and `deliveries` in **one transaction** (outbox pattern).
- Connection string comes from env `DATABASE_URL`. Support **both** the URI form (`postgres://user:pass@host/db`) and the key=value form. Convert URI to a Npgsql connection string in `Db`. Always enforce `SSL Mode=Require` for non-localhost hosts.
- Neon suspends idle compute: enable Npgsql retry-friendly settings (`Timeout=30`, `Command Timeout=30`) and retry the initial connection on startup (max 5 tries, 2s apart).

## 4. Web / HTTP Rules

- **htmx pattern:** routes that serve the UI return **HTML fragments** (partial views) when the `HX-Request` header is present, and a full page otherwise.
- Mutations use `hx-post` / `hx-delete` with `hx-target` and `hx-swap`. Return the updated fragment, not JSON.
- Live feed uses **SSE** via htmx's SSE extension (`hx-ext="sse" sse-connect=... sse-swap=...`). If the extension is not vendored, use a minimal 15-line vanilla `EventSource` script instead. Do not add libraries.
- Ingest endpoint `/h/{slug}`:
  - accepts any HTTP method
  - reads the raw body up to **256 KB**; larger returns `413`
  - returns `202 Accepted` with a small JSON body `{ "id": "<uuid>" }`
  - returns `404` for an unknown slug (do not leak whether other slugs exist beyond that)
  - honors an `Idempotency-Key` header: a duplicate key for the same endpoint returns `200` with the original id and creates nothing new
- **Rate limiting:** use the built-in `Microsoft.AspNetCore.RateLimiting` fixed-window limiter, partitioned by slug, 60 requests/min. Return `429` with `Retry-After`.
- Always HTML-encode captured data when rendering (Razor does this by default; never use `Html.Raw` on captured content).
- Add basic security headers middleware: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`.
- Health: `/healthz` returns 200 with no DB access. `/readyz` runs `SELECT 1`.

## 5. Delivery Worker Rules

- Implemented as `BackgroundService`. Poll every `Delivery:PollIntervalSeconds`.
- Use a single named `HttpClient` from `IHttpClientFactory` with the configured timeout.
- Outbound request:
  - method `POST` to the endpoint's `target_url`
  - body is the captured raw body
  - headers: `Content-Type` preserved, `X-HookRelay-Id`, `X-HookRelay-Timestamp` (unix seconds), `X-HookRelay-Signature: sha256=<hex>`
  - signature is `HMACSHA256(secret, $"{timestamp}.{body}")`
- Success is any 2xx. Everything else (including timeouts and network errors) counts as a failure.
- Record **every attempt** in `delivery_attempts` (status code, error text truncated to 500 chars, duration in ms).
- Backoff: `min(2^attempt × 5s, 1h)` plus 0–20% random jitter. Max attempts from config (default 6). After the last failure set status `dead`.
- **SSRF protection:** reject target URLs that resolve to loopback, link-local, or private ranges (127.0.0.0/8, 10/8, 172.16/12, 192.168/16, 169.254/16, ::1, fc00::/7). Validate on endpoint creation AND resolve-and-check at send time. Only `http`/`https` schemes.
- Handle `stoppingToken` cleanly. Finish or release in-flight work on shutdown and never leave rows stuck in `delivering` on a graceful stop.

## 6. Code Style

- File-scoped namespaces. `var` when the type is obvious.
- Prefer early returns over nesting.
- Log with `ILogger<T>` using structured templates (`"Delivery {DeliveryId} failed"`), never string interpolation in log calls.
- No comments that restate the code. Comment only the "why", especially around concurrency, SQL claim logic, and security decisions.
- Keep methods under about 40 lines. Extract when longer.

## 7. Testing Rules

- Unit-test: `RetryPolicy` (delay bounds, cap, max attempts), `HmacSigner` (known vectors), and SSRF URL validation.
- Tests must not need a live database. Pure logic only, unless a phase in the Roadmap says otherwise.
- Run: `docker compose run --rm sdk dotnet test`. Tests must pass before you mark a Roadmap phase complete.

## 8. Docker & Deployment Rules

- The `Dockerfile` is multi-stage, runs as a **non-root** user, and honors Render's `PORT` env var (bind `http://+:${PORT:-8080}`).
- Never commit secrets. `.env` is git-ignored, `.env.example` is committed.
- Do not change the Dockerfile's structure without a reason recorded in the commit message.

## 9. Workflow Rules for the Agent

1. Before each task, read `/.agents/Roadmap/SKILL.md` and identify the current phase.
2. Work in **small, verifiable steps**. After each step: build (`dotnet build`), then run the relevant tests.
3. Do not skip ahead of the current phase.
4. When a phase's **Definition of Done** is met, tick its checkboxes in the Roadmap file and stop for user review.
5. If a requirement is ambiguous or conflicts with these rules, **ask the user**. Do not guess on security or data-loss decisions.
6. Keep the README accurate: if you change behavior, routes, or config, update the README in the same change.
7. Commit messages: Conventional Commits (`feat:`, `fix:`, `chore:`, `test:`, `docs:`).

## 10. Definition of "Good" for This Project

Small, readable, correct, and demonstrably robust. A reviewer opening any file should understand it in under a minute. Prefer clarity over cleverness and fewer moving parts over abstraction.