import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router";
import { bookingsApi, type Booking } from "../api/bookings";
import { useAuth } from "../auth/AuthContext";
import { canCancel, canPay, Countdown, formatDate, money, StatusBadge } from "../components/BookingBits";
import { useToast } from "../components/Toasts";
import { usePush } from "../push/PushProvider";
import { useBookingActions } from "./useBookingActions";

const REFRESH_MS = 15_000; // also refreshed immediately when a push arrives

export function CustomerBookings() {
  const { session } = useAuth();
  const toast = useToast();
  const push = usePush();
  const [bookings, setBookings] = useState<Booking[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    if (!session) return;
    try { setBookings(await bookingsApi.list(session.token)); setError(null); }
    catch (e) { setError(e instanceof Error ? e.message : "Couldn't load bookings."); }
  }, [session]);

  const { pay, cancel, busyId } = useBookingActions(load);

  useEffect(() => { void load(); const t = setInterval(load, REFRESH_MS); return () => clearInterval(t); }, [load]);
  useEffect(() => push.onPush(() => void load()), [push, load]);

  async function create(e: FormEvent) {
    e.preventDefault();
    if (!session) return;
    setCreating(true);
    try {
      const b = await bookingsApi.create(session.token, name);
      setName("");
      toast("ok", "Booking created", `Pay ${money(b)} within 30 minutes to confirm it.`);
      await load();
    } catch (err) {
      toast("error", "Couldn't create booking", err instanceof Error ? err.message : undefined);
    } finally {
      setCreating(false);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>My bookings</h1>
          <p className="muted">An Umrah performed on behalf of your loved one. Fixed price: 500.00 SAR.</p>
        </div>
      </div>

      <NotificationsBanner />

      <form className="card new-booking" onSubmit={create}>
        <h2>New booking</h2>
        <div className="row grow">
          <label className="grow">Pilgrim's full name
            <input required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Ahmed bin Abdullah" />
          </label>
          <button className="btn btn-primary" disabled={creating || !name.trim()}>{creating ? "Creating…" : "Create booking"}</button>
        </div>
        <p className="muted small">You'll have 30 minutes to pay. Unpaid bookings expire automatically.</p>
      </form>

      {error && <p className="alert alert-error">{error}</p>}
      {bookings === null && !error && <p className="muted">Loading…</p>}
      {bookings?.length === 0 && <div className="card empty">No bookings yet. Create your first one above.</div>}

      <ul className="booking-list">
        {bookings?.map((b) => (
          <li key={b.id} className="card booking">
            <div className="booking-main">
              <Link to={`/bookings/${b.id}`} className="booking-name">{b.pilgrimName}</Link>
              <div className="booking-meta">
                <StatusBadge status={b.status} />
                <span className="muted small">{formatDate(b.createdAtUtc)}</span>
                {b.status === "PendingPayment" && <Countdown deadline={b.paymentDeadlineUtc} />}
              </div>
            </div>
            <div className="booking-side">
              <span className="price">{money(b)}</span>
              <div className="row">
                {canPay(b) && <button className="btn btn-primary" disabled={busyId === b.id} onClick={() => pay(b)}>Pay</button>}
                {canCancel(b) && <button className="btn btn-danger" disabled={busyId === b.id} onClick={() => cancel(b)}>Cancel</button>}
              </div>
            </div>
          </li>
        ))}
      </ul>
    </>
  );
}

function NotificationsBanner() {
  const push = usePush();
  if (push.state === "on") return null;
  const text = {
    off: "Turn on notifications to hear when a performer accepts your booking.",
    working: "Turning on notifications…",
    blocked: "Notifications are blocked for this site. Allow them in your browser settings to get updates.",
    unsupported: "This browser doesn't support push notifications. The list still updates automatically.",
  }[push.state];
  return (
    <div className="card banner">
      <span>🔔 {text}</span>
      {push.state === "off" && <button className="btn" onClick={() => push.enable()}>Turn on</button>}
    </div>
  );
}
