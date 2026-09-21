# 🪝 HookRelay

> A tiny, reliable webhook relay and inspector built with **ASP.NET Core 8**, **htmx**, **Bootstrap**, and **PostgreSQL**.

Capture incoming webhooks on unique endpoints, inspect them live, and **reliably forward** them to your target URLs with signed payloads, automatic retries, and a dead-letter queue.

**Live demo:** _https://hookrelay.onrender.com_ (add after deploy)
> The free tier sleeps after 15 minutes idle. The first request may take about 50 seconds.

---

## ✨ Features

| Feature | Description |
|---|---|
| **Endpoints** | Create unique capture URLs (`/h/{slug}`) |
| **Live inspector** | Incoming requests stream to the UI via Server-Sent Events, no page refresh |
| **Reliable forwarding** | Each captured request is forwarded to a configured target URL |
| **Retry with backoff** | Failed deliveries retry with exponential backoff plus jitter (max 6 attempts) |
| **Dead-letter queue** | Exhausted deliveries move to DLQ and can be replayed manually |
| **HMAC signing** | Outbound requests carry `X-HookRelay-Signature` (SHA-256) |
| **Idempotency** | Duplicate inbound deliveries (same `Idempotency-Key`) are ignored |
| **Rate limiting** | Per-endpoint fixed-window limiter |
| **Health checks** | `/healthz` (liveness) and `/readyz` (DB readiness) |
| **Auto-migrations** | Plain SQL migrations applied on startup, no ORM |

---

## 🧱 Tech Stack

- **Runtime:** .NET 8 (LTS), ASP.NET Core Minimal APIs
- **UI:** Razor Pages (partials only) + [htmx](https://htmx.org) + Bootstrap 5 (CDN)
- **Database:** PostgreSQL on Neon (free tier)
- **Data access:** [Npgsql](https://www.npgsql.org) + [Dapper](https://github.com/DapperLib/Dapper), with no EF Core
- **Queue:** PostgreSQL `FOR UPDATE SKIP LOCKED`, no Redis or RabbitMQ
- **Background work:** `BackgroundService` plus `System.Threading.Channels`
- **Container:** Docker multi-stage build, non-root user
- **Hosting:** Render (free web service, Docker runtime)

**NuGet dependencies (only 2):** `Npgsql`, `Dapper`
Everything else (rate limiting, health checks, HTTP client, logging, JSON) is built into the framework.

---

## 🏗️ Architecture

```
                ┌───────────────────────────────┐
  Third-party   │        ASP.NET Core App       │
  service ─────▶│  POST /h/{slug}  (Ingest API) │
                │      │ 1. validate + rate limit│
                │      │ 2. idempotency check    │
                │      ▼                         │
                │  ┌────────────── TX ────────┐  │
                │  │ INSERT captured_requests │  │
                │  │ INSERT deliveries (queue)│  │   ← outbox pattern
                │  └──────────────────────────┘  │
                │      │ publish event           │
                │      ▼                         │
                │  Channel<Event> ──▶ SSE ──▶ 🖥️ htmx UI (live feed)
                │                                │
                │  DeliveryWorker (BackgroundService)
                │   • SELECT … FOR UPDATE SKIP LOCKED
                │   • sign + POST to target
                │   • success → done
                │   • fail → backoff + jitter → retry
                │   • max attempts → dead-letter
                └──────────────┬─────────────────┘
                               ▼
                       Neon PostgreSQL
```

### Request lifecycle

1. **Ingest:** `POST /h/{slug}` receives any method, body, and headers.
2. **Guard:** The rate limiter runs, then a body-size cap (256 KB), then the idempotency check.
3. **Persist atomically:** In a single transaction the app writes the captured request and a `deliveries` row with status `pending`. This is the **transactional outbox**.
4. **Notify:** The app publishes an in-memory event so SSE clients see the request immediately.
5. **Deliver:** `DeliveryWorker` claims due rows with `SKIP LOCKED`, sends the signed request, and records each attempt.
6. **Retry or DLQ:** On failure the next attempt is scheduled with backoff. After the last attempt the delivery goes to `dead`.
7. **Replay:** From the UI, a single click resets a `dead` delivery back to `pending`.

### Retry schedule

`delay = min(2^attempt × 5s, 1h) + random jitter (0–20%)`

| Attempt | Approx. delay |
|---|---|
| 1 | immediate |
| 2 | 10 s |
| 3 | 20 s |
| 4 | 40 s |
| 5 | 80 s |
| 6 | 160 s → then **dead** |

---

## 📁 Folder Structure

```
hookrelay/
├── .agents/
│   ├── Instructions/SKILL.md      # Rules for the AI agent (how to code here)
│   └── Roadmap/SKILL.md           # Phased build plan for the AI agent
├── src/
│   └── HookRelay/
│       ├── Program.cs             # Composition root, DI, middleware, route mapping
│       ├── HookRelay.csproj
│       ├── appsettings.json
│       ├── Config/
│       │   └── AppOptions.cs      # Strongly-typed options
│       ├── Domain/
│       │   ├── Endpoint.cs
│       │   ├── CapturedRequest.cs
│       │   └── Delivery.cs
│       ├── Data/
│       │   ├── Db.cs              # NpgsqlDataSource wrapper
│       │   ├── Migrator.cs        # Runs /Migrations/*.sql on startup
│       │   ├── EndpointRepository.cs
│       │   ├── CaptureRepository.cs
│       │   └── DeliveryRepository.cs
│       ├── Migrations/
│       │   └── 001_init.sql
│       ├── Features/
│       │   ├── Ingest/IngestEndpoints.cs
│       │   ├── Endpoints/EndpointRoutes.cs      # CRUD + UI partial routes
│       │   ├── Deliveries/DeliveryRoutes.cs     # replay, list
│       │   └── Stream/StreamEndpoints.cs        # SSE
│       ├── Services/
│       │   ├── DeliveryWorker.cs                # BackgroundService
│       │   ├── RetryPolicy.cs                   # backoff + jitter (pure, testable)
│       │   ├── HmacSigner.cs
│       │   └── EventBus.cs                      # Channel-based pub/sub
│       ├── Views/                               # Razor Pages / partials
│       │   ├── Pages/
│       │   │   ├── Index.cshtml
│       │   │   └── Inspect.cshtml
│       │   └── Partials/
│       │       ├── _EndpointList.cshtml
│       │       ├── _RequestRow.cshtml
│       │       └── _DeliveryBadge.cshtml
│       └── wwwroot/
│           ├── css/site.css
│           └── js/htmx.min.js
├── tests/
│   └── HookRelay.Tests/           # xUnit: RetryPolicy, HmacSigner, Ingest
├── .devcontainer/
│   └── devcontainer.json          # VS Code: develop inside the .NET SDK container
├── .dockerignore
├── .gitignore
├── Dockerfile
├── docker-compose.yml
├── render.yaml                    # Render blueprint (optional)
└── README.md
```

---

## 🗄️ Data Model

```sql
endpoints          (id, slug UNIQUE, name, target_url, signing_secret, created_at)
captured_requests  (id, endpoint_id FK, method, headers JSONB, body TEXT,
                    query TEXT, idempotency_key, received_at)
deliveries         (id, request_id FK, status, attempt_count, next_attempt_at,
                    last_status_code, last_error, updated_at)
delivery_attempts  (id, delivery_id FK, attempted_at, status_code, error, duration_ms)
```

`status ∈ pending | delivering | succeeded | dead`

---

## 🌐 Routes

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/` | Dashboard, list of endpoints |
| `POST` | `/endpoints` | Create endpoint (htmx, returns list partial) |
| `DELETE` | `/endpoints/{id}` | Delete endpoint |
| `GET` | `/endpoints/{slug}` | Inspector page |
| `ANY` | `/h/{slug}` | **Ingest** captured webhook |
| `GET` | `/endpoints/{slug}/stream` | SSE live feed |
| `POST` | `/deliveries/{id}/replay` | Replay a dead or failed delivery |
| `GET` | `/healthz` | Liveness |
| `GET` | `/readyz` | Readiness (checks DB) |

---

## 🚀 Getting Started (No .NET installed locally)

**Prerequisites:** Docker Desktop (with WSL2 integration on Windows) and, optionally, VS Code with the *Dev Containers* extension.

### Quick Start

```bash
# 1. Clone the repo and enter the folder
git clone https://github.com/programmer-bell/hookrelay.git
cd hookrelay

# 2. (Optional) create your env file. The defaults use the bundled local Postgres.
cp .env.example .env

# 3. Build and start everything (app + local Postgres)
docker compose up --build
```

Open **http://localhost:8080**. Migrations run automatically on first boot.

### Day-to-Day Docker Commands

| Task | Command |
|---|---|
| Start in the background | `docker compose up -d` |
| Start and rebuild the image | `docker compose up --build -d` |
| View live logs | `docker compose logs -f app` |
| Stop (keeps your data) | `docker compose down` |
| Stop and **wipe the database** | `docker compose down -v` |
| Recreate from scratch (clean DB and fresh image) | `docker compose down -v && docker compose up --build -d` |
| Force-recreate containers only | `docker compose up -d --force-recreate` |
| Hot-reload dev mode | `docker compose --profile dev up dev` |
| Check running containers | `docker compose ps` |

### Running .NET Commands Without Installing .NET

```bash
docker compose run --rm sdk dotnet build
docker compose run --rm sdk dotnet test
docker compose run --rm sdk dotnet format
```

### Alternative: VS Code Dev Container

1. Open the folder in VS Code
2. Press `F1` and run **Dev Containers: Reopen in Container**
3. In the integrated terminal:
```bash
   dotnet run --project src/HookRelay
```

### Try It Out with a Dummy Target URL

Once the app is running, you can test the full flow (capture, forward, retry) without building any second service.

**1. Get a free dummy target.** Open [webhook.site](https://webhook.site) and copy your unique URL, for example `https://webhook.site/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`. It will display every request HookRelay forwards.

**2. Create an endpoint.** Go to http://localhost:8080, fill in:
- **Name:** `demo`
- **Target URL:** your webhook.site URL

Copy the generated ingest URL (it looks like `http://localhost:8080/h/abc123xyz9`).

**3. Send a test webhook.**
```bash
curl -X POST http://localhost:8080/h/<your-slug> \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-001" \
  -d '{"event":"order.created","order_id":42}'
```
You'll get `202 Accepted`. The request appears live in the inspector, and the forwarded copy (with the `X-HookRelay-Signature` header) shows up on webhook.site.

**4. Test idempotency.** Run the same command again with `Idempotency-Key: demo-001`. It returns `200` with the original id, and nothing new is created.

**5. Test retries and the dead-letter queue.** Edit the endpoint's target URL to a URL that always fails, for example `https://httpbin.org/status/500`, then send another webhook. Watch the inspector show attempts 1 to 6 with growing delays, then the status change to `dead`. Change the target back to your webhook.site URL and click **Replay** to deliver it.

> **Note:** Target URLs pointing to `localhost` or private IPs are rejected by design (SSRF protection), so always use a public dummy URL like the ones above.

## ⚙️ Configuration

| Variable | Description | Example |
|---|---|---|
| `DATABASE_URL` | Postgres connection string | `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require` |
| `ASPNETCORE_URLS` | Bind address (Render sets `PORT`) | `http://+:8080` |
| `Delivery__MaxAttempts` | Max attempts before DLQ | `6` |
| `Delivery__TimeoutSeconds` | Outbound HTTP timeout | `10` |
| `Delivery__PollIntervalSeconds` | Worker poll interval | `2` |

---

## ☁️ Deploy to Render (Free)

1. Create a free project on **[Neon](https://neon.tech)** and copy the connection string.
2. Push this repo to GitHub.
3. On Render: **New → Web Service → Build from Dockerfile**, choose the **Free** plan.
4. Add environment variable `DATABASE_URL` (Neon connection string).
5. Set the health check path to `/healthz`.
6. Deploy. Migrations run automatically on first boot.

> **Keep-alive tip:** Use a free uptime pinger (for example UptimeRobot) against `/healthz` every 10 minutes to reduce cold starts. Neon also auto-suspends compute, so the first query after idle is slightly slower. The Npgsql connection retry handles this.

---

## 🧪 Testing

```bash
docker compose run --rm sdk dotnet test
```
Covers the retry policy math, HMAC signature correctness, and idempotency behavior.

---

## 🎯 Backend Concepts Showcased

- Transactional **outbox pattern**
- **Postgres as a queue** (`SKIP LOCKED`) with a safe multi-worker design
- **Exponential backoff + jitter** and a **dead-letter queue**
- **HMAC-SHA256** request signing
- **Idempotency keys**
- **Server-Sent Events** with an in-process `Channel<T>` event bus
- Built-in **rate limiting** middleware
- **Health/readiness** probes
- **Graceful shutdown** handling
- Raw SQL migrations, with no ORM magic
- Multi-stage, **non-root** Docker image

---

## 🗺️ Roadmap Ideas

- API key auth and multi-user accounts
- Per-endpoint custom headers and payload transforms
- Prometheus `/metrics`
- Signature verification for inbound providers (Stripe and GitHub styles)

## 📄 License
MIT — see [LICENSE](LICENSE).
