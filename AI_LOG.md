# AI usage log

Running notes for the "How I used AI" section of FIXES.md. Add one entry per step.

| Step | What I asked | What I accepted | What I changed or rejected, and why |
|---|---|---|---|
| Planning | Analyze the assignment and make a full plan | 3-phase, step-by-step plan; PostgreSQL outbox for pushes; conditional SQL updates for races | Chose Supabase for production Postgres instead of Render Postgres (Render's free DB expires after 30 days). Renamed the project to BadalBooking so it can't mix with an older attempt. |
| 1.1 Skeleton | Create the solution skeleton | Solution, API and test project, `/ping`, smoke test, `.gitignore` with secret rules | |
| 1.1 Question | Why local Postgres instead of connecting Supabase now? | Local Docker Postgres for development, tests and CI; Supabase only for the deployed app (Phase 3), same migrations for both | Accepted. Reasons: frequent schema changes, disposable test data, speed, free-tier limits, CI must not need production secrets. |
| 1.2 Database | Local Postgres, entities, DbContext, first migration | Guid v7 ids, integer halalas, server-only price via `Booking.Create`, stored payment deadline, xmin concurrency token, check constraints, `app` schema | Reviewed the generated migration: EF had put its history table in `public` (exposed by Supabase's REST API), so it was moved into `app`. Used `docker run` because the compose plugin isn't installed locally. |
| 1.2 Secrets | Put all current secrets in a `.env` with comments on where each one goes | Git-ignored `.env` (chmod 600) + committed `.env.example`; small `DotEnv` loader used only in Development; real env vars always win | Moved the connection string out of `appsettings.Development.json` so no committed file holds one. Wrote a 30-line loader instead of adding a package. Production values stay in Render/GitHub/Vercel, never in `.env`. |
| 1.3 Auth | Register, login, JWT | Customer-only registration, PBKDF2 hashing, HS256 JWT with only `sub`/`role`/`jti`, unique-index-based duplicate check, same 401 + equal timing for unknown email vs wrong password, per-IP rate limit, ProblemDetails errors | |
| 1.3 Deploy-readiness | "Backend on Render, frontend on Vercel — make sure it works when deployed" | Bind to Render's `PORT`; trust `X-Forwarded-For` only when `RENDER=true`; strict CORS with exact origins, startup fails on `*`/non-https in production | Caught that behind Render's proxy every user would share one IP, so the login rate limit would lock everyone out. Proxy header handling must be re-checked on the live Render URL in step 3.3. |
