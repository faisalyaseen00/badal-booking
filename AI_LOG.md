# AI usage log

Running notes for the "How I used AI" section of FIXES.md. Add one entry per step.

| Step | What I asked | What I accepted | What I changed or rejected, and why |
|---|---|---|---|
| Planning | Analyze the assignment and make a full plan | 3-phase, step-by-step plan; PostgreSQL outbox for pushes; conditional SQL updates for races | Chose Supabase for production Postgres instead of Render Postgres (Render's free DB expires after 30 days). Renamed the project to BadalBooking so it can't mix with an older attempt. |
| 1.1 Skeleton | Create the solution skeleton | Solution, API and test project, `/ping`, smoke test, `.gitignore` with secret rules | |
