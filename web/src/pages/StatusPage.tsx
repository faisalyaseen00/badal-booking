import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api/client";
import { config } from "../config";
import { formatDate } from "../components/BookingBits";

interface Health { status: string; database: string; version: string; checkedAtUtc: string }
interface DemoBooking { reference: string; status: string; amount: number; currency: string; createdAtUtc: string }
interface DemoBookings { configured: boolean; demoCustomer: string | null; bookings: DemoBooking[] }

const REFRESH_MS = 30_000;

// Public status page (bonus): API + database health, and the demo customer's last 5 bookings.
// No sign-in. The API only answers this page's origin (CORS), and returns no personal data.
export function StatusPage() {
  const [health, setHealth] = useState<Health | null>(null);
  const [healthError, setHealthError] = useState<string | null>(null);
  const [latencyMs, setLatencyMs] = useState<number | null>(null);
  const [demo, setDemo] = useState<DemoBookings | null>(null);
  const [checkedAt, setCheckedAt] = useState<Date | null>(null);

  const load = useCallback(async () => {
    const started = performance.now();
    try {
      setHealth(await apiFetch<Health>("/health"));
      setHealthError(null);
    } catch (e) {
      setHealth(null);
      setHealthError(e instanceof Error ? e.message : "Unreachable");
    }
    setLatencyMs(Math.round(performance.now() - started));
    try { setDemo(await apiFetch<DemoBookings>("/status/demo-bookings")); } catch { setDemo(null); }
    setCheckedAt(new Date());
  }, []);

  useEffect(() => { void load(); const t = setInterval(load, REFRESH_MS); return () => clearInterval(t); }, [load]);

  const healthy = health?.status === "Healthy";

  return (
    <>
      <div className="page-head">
        <div>
          <h1>System status</h1>
          <p className="muted">Live check of the Badal Booking API on Render and its PostgreSQL database on Supabase.</p>
        </div>
        <button className="btn" onClick={() => load()}>Check now</button>
      </div>

      <div className="status-grid">
        <div className="card stat">
          <span className="muted small">API</span>
          <strong className={healthError ? "bad" : "ok"}>{healthError ? "Unreachable" : health ? "Online" : "Checking…"}</strong>
          <span className="muted small">{latencyMs !== null && `${latencyMs} ms`}</span>
        </div>
        <div className="card stat">
          <span className="muted small">Database</span>
          <strong className={health?.database === "Up" ? "ok" : healthError || health ? "bad" : ""}>
            {health?.database ?? (healthError ? "Unknown" : "Checking…")}
          </strong>
          <span className="muted small">{healthy ? "Healthy" : health ? "Unhealthy" : ""}</span>
        </div>
        <div className="card stat">
          <span className="muted small">Deployed version</span>
          <strong className="mono">{health?.version ?? "–"}</strong>
          <span className="muted small">{checkedAt && `checked ${checkedAt.toLocaleTimeString()}`}</span>
        </div>
      </div>
      {healthError && <p className="alert alert-error">{healthError} The free Render plan sleeps when idle; the first check can take up to a minute.</p>}

      <div className="card">
        <h2>Demo customer – last 5 bookings</h2>
        {!demo && <p className="muted">Loading…</p>}
        {demo && !demo.configured && <p className="muted">No demo customer is configured on the server (Demo__CustomerEmail).</p>}
        {demo?.configured && (
          <>
            <p className="muted small">Customer {demo.demoCustomer}. References only – no personal data.</p>
            {demo.bookings.length === 0 ? <p className="muted">No bookings yet.</p> : (
              <table className="table">
                <thead><tr><th>Reference</th><th>Status</th><th>Amount</th><th>Created</th></tr></thead>
                <tbody>
                  {demo.bookings.map((b) => (
                    <tr key={b.reference}>
                      <td className="mono">{b.reference}</td>
                      <td><span className={`status status-${b.status}`}>{b.status}</span></td>
                      <td>{b.amount.toFixed(2)} {b.currency}</td>
                      <td>{formatDate(b.createdAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>
      <p className="muted small">API: {config.apiBaseUrl} · refreshes every 30 s</p>
    </>
  );
}
