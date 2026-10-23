import { useCallback, useEffect, useState } from "react";
import { ApiError } from "../api/client";
import { performerApi, type AcceptedBooking, type AvailableBooking } from "../api/performer";
import { useAuth } from "../auth/AuthContext";
import { formatDate } from "../components/BookingBits";
import { useToast } from "../components/Toasts";

const REFRESH_MS = 10_000; // other performers may take bookings at any moment

export function PerformerHome() {
  const { session } = useAuth();
  const toast = useToast();
  const [available, setAvailable] = useState<AvailableBooking[] | null>(null);
  const [mine, setMine] = useState<AcceptedBooking[] | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!session) return;
    try {
      const [a, m] = await Promise.all([performerApi.available(session.token), performerApi.mine(session.token)]);
      setAvailable(a); setMine(m); setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't load bookings.");
    }
  }, [session]);

  useEffect(() => { void load(); const t = setInterval(load, REFRESH_MS); return () => clearInterval(t); }, [load]);

  async function accept(b: AvailableBooking) {
    if (!session) return;
    setBusyId(b.id);
    try {
      await performerApi.accept(session.token, b.id);
      toast("ok", "Booking accepted", `You will perform the Umrah for ${b.pilgrimName}. The customer has been notified.`);
    } catch (e) {
      // 409 = another performer was faster, or the customer cancelled. Only one performer can win.
      if (e instanceof ApiError && e.status === 409) toast("info", "Already taken", "Another performer accepted this booking first, or it was cancelled.");
      else toast("error", "Couldn't accept", e instanceof Error ? e.message : undefined);
    } finally {
      setBusyId(null);
      await load();
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Available bookings</h1>
          <p className="muted">Paid bookings waiting for a performer, oldest first. The first performer to accept gets the booking.</p>
        </div>
        <button className="btn" onClick={() => load()}>Refresh</button>
      </div>

      {error && <p className="alert alert-error">{error}</p>}
      {available === null && !error && <p className="muted">Loading…</p>}
      {available?.length === 0 && <div className="card empty">No bookings are waiting right now. This list refreshes automatically.</div>}

      <ul className="booking-list">
        {available?.map((b) => (
          <li key={b.id} className="card booking">
            <div className="booking-main">
              <span className="booking-name">Umrah for {b.pilgrimName}</span>
              <div className="booking-meta">
                <span className="status status-Paid">Paid</span>
                <span className="muted small">Booked {formatDate(b.createdAtUtc)}</span>
              </div>
            </div>
            <button className="btn btn-primary" disabled={busyId === b.id} onClick={() => accept(b)}>
              {busyId === b.id ? "Accepting…" : "Accept"}
            </button>
          </li>
        ))}
      </ul>

      <h2 className="section-title">My accepted bookings</h2>
      {mine?.length === 0 && <div className="card empty">You haven't accepted any bookings yet.</div>}
      <ul className="booking-list">
        {mine?.map((b) => (
          <li key={b.id} className="card booking">
            <div className="booking-main">
              <span className="booking-name">Umrah for {b.pilgrimName}</span>
              <div className="booking-meta">
                <span className="status status-Accepted">Accepted by you</span>
                <span className="muted small">Accepted {formatDate(b.acceptedAtUtc)}</span>
              </div>
            </div>
            <span className="mono muted small">{b.id.slice(0, 8)}</span>
          </li>
        ))}
      </ul>
    </>
  );
}
