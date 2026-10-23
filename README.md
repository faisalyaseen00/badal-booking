# Badal Booking

Umrah-by-proxy booking platform built for the **Labaik Badal .NET assignment**.
Customers book an Umrah for a pilgrim at a fixed 500 SAR, pay, and can cancel until a performer
accepts. Approved performers accept paid bookings (only one can win). Unpaid bookings expire after
30 minutes. Customers get push notifications when their booking is accepted or expires.

| | |
|---|---|
| API | https://badal-booking.onrender.com/health |
| Web app | https://badal-booking.vercel.app (status page: `/status`) |
| Bugs, design, deployment, AI usage | [FIXES.md](FIXES.md) · [AI_LOG.md](AI_LOG.md) |

The free Render plan sleeps when idle: the first request can take about a minute.

## Architecture

```
Browser (React, Vercel) ──HTTPS──▶ API (ASP.NET Core 10, Docker on Render) ──▶ PostgreSQL 17 (Supabase)
   │  Firebase JS SDK                 │  JWT auth · bookings · payments (simulated)
   │  (sign-in, FCM token)            │  background workers: expiry (60 s) · push outbox (5 s)
   └──────────── push ◀── Firebase Cloud Messaging ◀── Firebase Admin SDK
```

- **Roles:** Customer (books, pays, cancels), Performer (accepts; via application + admin approval), Admin (approves performers; created from server settings).
- **Correctness:** every state change is a conditional SQL update; database constraints back up the rules; notifications use a transactional outbox.

## Repository layout

```
src/BadalBooking.Api/      API: Domain, Data (EF Core + migrations), Features (endpoints), Infrastructure, Background workers
tests/BadalBooking.Tests/  80 integration tests (WebApplicationFactory + Testcontainers PostgreSQL)
web/                       React 19 + Vite + TypeScript web app (deployed to Vercel)
Dockerfile                 API image (Render)
.github/workflows/ci.yml   build + test on every push; deploy to Render from main
```

## Run locally

Prerequisites: .NET 10 SDK, Docker, Node 24.

```bash
cp .env.example .env            # fill in Jwt__Key (32+ chars) and, optionally, Firebase values
docker compose up -d db         # PostgreSQL 17 on port 5433 (or the docker run command in compose.yaml)
dotnet run --project src/BadalBooking.Api   # http://localhost:5080 – migrates and seeds on startup
```

```bash
cd web && cp .env.example .env.local   # VITE_API_BASE_URL=http://localhost:5080 + Firebase web config
npm ci && npm run dev                  # http://localhost:5173
```

## Tests

```bash
dotnet test        # needs Docker: each run starts its own throwaway PostgreSQL container
```

Covers ownership, fixed price, idempotent payment, parallel payment/accept/cancel races, the
30-minute expiry (fake clock), Firebase account-linking rules, devices, the outbox and push worker,
performer applications, and health.

## Main endpoints

| Method | Path | Who |
|---|---|---|
| POST | `/auth/register`, `/auth/login`, `/auth/firebase` | public (rate-limited) |
| POST | `/auth/logout`, `/devices`; DELETE `/devices/{id}` | signed in |
| POST/GET | `/bookings`, `/bookings/{id}`, `/bookings/{id}/pay`, `/bookings/{id}/cancel` | Customer (own data only) |
| GET/POST | `/bookings/available`, `/bookings/{id}/accept`, `/performer/bookings` | Performer |
| POST/GET | `/performer-applications`, `/performer-applications/me` | Customer |
| GET/POST | `/admin/performer-applications`, `…/{id}/approve`, `…/{id}/reject` | Admin |
| GET | `/health`, `/status/demo-bookings`, `/ping` | public |

## Configuration

All secrets come from environment variables (see `.env.example` and `web/.env.example`; full list in
[FIXES.md § Deployment notes](FIXES.md#3-deployment-notes)). Nothing secret is committed.
