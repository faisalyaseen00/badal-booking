# FIXES.md – Badal Booking (Labaik Badal assignment)

Umrah-by-proxy booking platform: ASP.NET Core 10 API, PostgreSQL, Firebase sign-in and push,
background jobs, React web app. No starter code was provided, so this was built from scratch.
Every bug below is either a **production bug class the design prevents** (with the test that
proves it), a **bug I found in my own code while building** (with how it was found), or an
**open issue I did not fix**.

| | |
|---|---|
| API (Render, Singapore) | https://badal-booking.onrender.com — `GET /health` |
| Web app + status page (Vercel) | https://badal-booking.vercel.app — status page at `/status` *(update if Vercel assigned a different domain)* |
| Database | Supabase PostgreSQL 17 (Session pooler) |
| Tests | 80 integration tests against a real PostgreSQL 17 container (`dotnet test`) |

---

## 1. Bugs: what → why dangerous → how fixed

### 1a. Production bug classes the assignment describes, prevented by design

| # | Bug | Why it's dangerous | How it's fixed | Proof (test) |
|---|---|---|---|---|
| 1 | **Client decides the price** (mass assignment) | Anyone pays 1 SAR for a 500 SAR Umrah | Request DTO has only `pilgrimName`; price comes from `Booking.Create` (50 000 halalas, SAR); DB check constraints `amount > 0`, `currency = 'SAR'` | `Client_cannot_choose_price_status_or_owner` (checks the DB row) |
| 2 | **Users see/change other users' data (IDOR)** | Read or cancel someone else's booking by guessing an id | User id only from the validated JWT; ownership filter inside every SQL query; 404 (not 403) for others' data; non-guessable UUIDv7 ids | `Customer_cannot_read/cancel/pay_another_customers_booking` |
| 3 | **Role taken from the request** | Self-register as Performer/Admin and accept or approve everything | Register always creates a Customer; role only from the DB; Performer via admin approval, Admin only from server settings | `Register_always_creates_a_customer…`, `Admin_cannot_be_created_by_registering` |
| 4 | **Two performers win the same booking** (read-then-write race) | Two people perform/claim the same paid booking | One conditional `UPDATE … WHERE status='Paid' AND performer_id IS NULL`; Postgres row lock decides; DB rule "Accepted ⇔ has performer" | `Ten_performers_accept_at_once_and_exactly_one_wins` |
| 5 | **Double payment** (double click / retry / parallel) | Customer charged twice | Required `Idempotency-Key`; same key replays the stored payment; conditional claim of `PendingPayment`; partial unique index "one Succeeded payment per booking" | `Ten_parallel_payments_…_exactly_one_payment`, `Database_refuses_a_second_successful_payment…` |
| 6 | **Cancel and accept at the same time both succeed** | Booking cancelled+refunded AND assigned | Both are conditional updates on the same row; exactly one wins; refund in the same transaction as the cancel | `Cancel_and_accept_racing_never_both_succeed` (15 rounds) |
| 7 | **Expiry job expires paid bookings / pay after the deadline** | Paid customers lose their booking, or pay for an expired one | Deadline stored once; job uses `status='PendingPayment' AND deadline <= now`, pay uses `deadline > now` (mutually exclusive); `FOR UPDATE SKIP LOCKED` batches; pay endpoint checks the deadline itself | `Payment_is_rejected_once_the_30_minute_deadline_is_reached`, `Paid_cancelled_and_accepted_bookings_are_never_expired` |
| 8 | **Firebase login takes over an account by email** | Attacker with an unverified Firebase email becomes the customer, or a performer | Admin SDK verifies signature/project/expiry/revocation; new links need `email_verified`; never links to Performer/Admin by email; existing link is never replaced | `FirebaseSignInTests` (11 tests) |
| 9 | **Push sent inside the HTTP request / before commit** | Slow requests; "accepted" pushes for changes that rolled back; lost pushes on crash | Outbox row written in the same SQL statement as accept/expiry; background worker sends after commit, retries, leases rows | `If_the_notification_cannot_be_queued_the_accept_is_rolled_back`, `PushTests` |
| 10 | **Invalid FCM tokens kept forever / valid ones deleted on outages** | Wasted sends, or users silently stop getting pushes | Delete only on Unregistered/SenderIdMismatch/InvalidArgument and only if the token still belongs to that user; retry temporary failures with backoff | `Tokens_FCM_reports_as_invalid_are_deleted…`, `Temporary_failures_keep_the_token…` |
| 11 | **Secrets in the repo / image** | Firebase admin key or DB password leaks → full takeover | All secrets from environment variables; `.env*` git-ignored; `.dockerignore`; history scanned before the first push | manual scan in step 3.3 (0 hits) |
| 12 | **Weak JWT validation / CORS `*`** | Forged tokens (`alg: none`), any website calling the API with users' tokens | HS256 only, issuer/audience/lifetime checked; startup refuses a short key; exact-origin CORS, startup refuses `*` or non-https in production | `Protected_endpoints_reject_missing_and_tampered_tokens`, startup checks |
| 13 | **Login reveals which emails exist** | Account enumeration for phishing/stuffing | Same 401 for unknown email and wrong password, with an equal-cost hash check | `Login_gives_the_same_401…` |

### 1b. Bugs I found in my own code while building

| Bug | Why dangerous | Fix | How it was found |
|---|---|---|---|
| Same-key payment retries sometimes got 409 | Client retrying a successful payment sees "failed" (no double charge, but wrong answer) | Re-check the idempotency key before any 409 | 10 parallel same-key requests in the step 1.5 demo; now a regression test |
| Expiry SQL returned `"Id"/"CustomerId"` but EF expected `id/customer_id` | Rows were expired but the job didn't know which → "expired" pushes would be lost | Return snake_case columns | Worker log showed errors although the DB looked right (step 1.7) |
| JWT lifetime checked with the system clock, tokens issued with the injected clock | Tokens wrongly valid/invalid whenever the clocks differ | Validate lifetime with the same `TimeProvider` | Predicted before running tests; proven: 32/41 tests failed without the fix |
| Concurrent first Firebase sign-ins: one in five got 409 "already linked" | A brand-new user tapping "sign in" twice sees an error | If the existing link is to the *same* uid, sign in instead of 409 | Intermittent failure of `Simultaneous_first_sign_ins_create_exactly_one_account` |
| React logout hook stored as state → infinite render loop | Blank customer page | Store it in a `ref` | Browser check of step 2.8 |
| Cancelled booking timeline showed "Paid" crossed out | Misleading UI | Show only "created → ended" for ended bookings | Browser check |
| Database not migrated → API crashed at startup | Every deploy with a new migration would crash | Apply migrations at startup (before workers), fail fast | Local crash in step 2.10 |
| `Max Pool Size` rejected by Npgsql 10 | API would crash on Render with the documented connection string | Use `Maximum Pool Size`; fixed the template | Supabase connection test, isolated option by option (step 3.2) |
| Npgsql logs "cannot open libgssapi_krb5" in the slim image | Scary error in production logs | Install `libgssapi-krb5-2` in the runtime image | Running the image in Production mode (step 3.1) |
| `tsconfig.tsbuildinfo` committed | Build cache noise in the repo | Ignored and removed | Review before committing step 2.8 |

### 1c. Open issues (not fixed) – known and documented

| Issue | Why it matters | Planned fix |
|---|---|---|
| **Login rate limit can be bypassed on Render** with a fake `X-Forwarded-For` (12 failed logins, no 429) | Brute-force of passwords is not throttled per client IP | Per-account failed-login limit (independent of IP) + read Render's real client IP after inspecting its headers. Deferred by decision. |
| JWTs can't be revoked before they expire (60 min) | A stolen token works until expiry; logout only removes the push token | Short-lived access token + refresh token with server-side revocation |
| Push delivery is at-least-once | A retry after partial success can show a notification twice | `eventId` is in the payload for client-side de-duplication; per-device delivery records would make it exact |
| Render free plan sleeps after ~15 min | Expiry and push workers pause; first request takes ~50 s | Workers catch up on wake-up and pay checks the deadline itself; a paid plan or external cron fixes it fully |
| Payments are simulated (not HyperPay) | No real money moves | `IPaymentGateway` is ready for a HyperPay implementation (checkout → server-side status check → reconciliation) |
| After admin approval the old JWT still says "Customer" | Applicant must sign in again | Documented in the UI; refresh tokens would solve it |
| Supabase free project pauses after 7 days idle | Live demo would fail | Open the project before review |

---

## 2. Firebase design

**Sign-in (`POST /auth/firebase`)**
1. The web app signs the user in with the Firebase JS SDK and sends the Firebase **ID token**.
2. The API verifies it with the **Firebase Admin SDK** `VerifyIdTokenAsync(checkRevoked: true)`: Google signature, audience/issuer = our project, expiry, and revocation. We never just decode the JWT.
3. Account rules, in order: known Firebase uid → that user (role from our DB) · email missing or not verified → 403 · email belongs to a Performer/Admin → 409 (never linked) · email belongs to a Customer → linked once (a second Firebase account can't replace it) · otherwise a new Customer.
4. The API returns **its own JWT** (same as password login). Invalid token → 401, Firebase unreachable → 503.
5. The service-account key is loaded **only** from `Firebase__ServiceAccountJson` (never a file in the repo); a key from another project is rejected at startup.

**Push notifications**
1. `POST /devices` saves the browser/phone FCM token for the signed-in user (atomic upsert; a shared device moves to the new user). `POST /auth/logout` removes it.
2. Accepting or expiring a booking writes a row to `notification_outbox` **in the same SQL statement** as the status change → committed together or not at all.
3. `PushWorker` (every 5 s, in the background, never in an HTTP request) claims due rows with a lease (`FOR UPDATE SKIP LOCKED`), looks up the customer's devices **at send time**, sends with `SendEachForMulticastAsync`, deletes tokens FCM reports as invalid, retries temporary failures with exponential backoff, and gives up after 8 attempts (row kept for investigation).

---

## 3. Deployment notes

- **API:** Render web service (Docker, Singapore, free). Dockerfile: multi-stage, non-root, no secrets. Health check `/health` (200 Healthy/Up or 503).
- **Database:** Supabase PostgreSQL 17, Session pooler (IPv4, supports migrations). Tables live in a private `app` schema; Supabase's Data API is off and `anon`/`authenticated` have no access.
- **Migrations:** applied automatically at API startup (EF lock prevents double-migration; failure stops startup so Render keeps the previous version).
- **CI/CD (GitHub Actions):** every push/PR builds and tests the API (Testcontainers), builds the web app and the Docker image. On push to `main`, only if all pass, it calls Render's deploy hook and waits until `/health` reports the tested commit.
- **Web app:** Vercel (root directory `web`), builds on every push to `main`.

**Environment variables (names only)**

| Where | Variables |
|---|---|
| Render (API) | `ConnectionStrings__Default`, `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, `Firebase__ProjectId`, `Firebase__ServiceAccountJson`, `Payments__Provider`, `Seed__AdminEmail`, `Seed__AdminPassword`, `Seed__PerformerEmail`, `Seed__PerformerPassword`, `Cors__AllowedOrigins__0`, `Demo__CustomerEmail` (Render sets `PORT`, `RENDER`, `RENDER_GIT_COMMIT`) |
| Vercel (web, public) | `VITE_API_BASE_URL`, `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, `VITE_FIREBASE_MESSAGING_SENDER_ID`, `VITE_FIREBASE_APP_ID`, `VITE_FIREBASE_VAPID_KEY` |
| GitHub Actions | `RENDER_DEPLOY_HOOK_URL` |

---

## 4. How I used AI

I built this with an AI coding assistant (Claude), step by step: for each step the AI wrote the code,
explained every file, ran the checks, and **I reviewed and decided before every commit**. The full
per-step record is in [`AI_LOG.md`](AI_LOG.md). Summary:

**What I asked:** a full plan first; then one small step at a time (skeleton → database → auth →
bookings → payment → accept/cancel → expiry → tests → Firebase → push → web app → deploy), each with a
demo I could show.

**What I accepted**
- Conditional SQL updates and database constraints as the real guard against races (not C# `if`s).
- The transactional outbox for push notifications instead of sending inside requests.
- Real PostgreSQL in tests (Testcontainers) instead of an in-memory database, so races are real.
- Integer halalas for money, UUIDv7 ids, a private `app` schema for Supabase.

**What I changed**
- Production database: Supabase instead of Render Postgres (Render's free DB expires after 30 days).
- Required a **verified email for every** Firebase sign-in, not only for linking (stops email squatting).
- Asked for a real React web app instead of a test page, and a **performer application + admin approval** flow instead of server-only performer accounts.
- Kept "Max Pool Size" → "Maximum Pool Size", JWT clock, and other fixes only after seeing them fail.

**What I rejected or deferred**
- Self-service performer sign-up without approval (anyone could accept every paid booking).
- A third-party `.env` package (a 30-line loader is enough and easier to review).
- The newest library releases (only versions at least two weeks old).
- The rate-limit fix behind Render's proxy: found and documented, but deferred (section 1c).

**How I checked the AI's work:** every step had a live demo (curl, browser, or real Firebase tokens),
the test suite ran before each commit, race tests were repeated several times, secrets were scanned
before pushing, and the deployed API was smoke-tested. Several bugs in section 1b were mistakes in the
AI-written code that these checks caught.
