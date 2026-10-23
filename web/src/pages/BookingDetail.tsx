import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { bookingsApi, type Booking } from "../api/bookings";
import { ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { canCancel, canPay, Countdown, formatDate, money, StatusBadge } from "../components/BookingBits";
import { usePush } from "../push/PushProvider";
import { useBookingActions } from "./useBookingActions";

// Steps shown in the timeline, in order. Cancelled/Expired end the timeline early.
const steps = [
  { key: "created", label: "Booking created" },
  { key: "paid", label: "Paid (500.00 SAR)" },
  { key: "accepted", label: "Accepted by a performer" },
] as const;

export function BookingDetail() {
  const { id = "" } = useParams();
  const { session } = useAuth();
  const push = usePush();
  const [booking, setBooking] = useState<Booking | null>(null);
  const [missing, setMissing] = useState(false);

  const load = useCallback(async () => {
    if (!session) return;
    try { setBooking(await bookingsApi.get(session.token, id)); }
    catch (e) { if (e instanceof ApiError && e.status === 404) setMissing(true); }
  }, [session, id]);

  const { pay, cancel, busyId } = useBookingActions(load);
  useEffect(() => { void load(); const t = setInterval(load, 15_000); return () => clearInterval(t); }, [load]);
  useEffect(() => push.onPush((bookingId) => { if (!bookingId || bookingId === id) void load(); }), [push, load, id]);

  // Someone else's booking and a non-existent one look the same (the API returns 404 for both).
  if (missing) return <div className="card narrow"><h1>Booking not found</h1><Link to="/bookings" className="btn">Back to my bookings</Link></div>;
  if (!booking) return <p className="muted">Loading…</p>;

  const reached = { created: true, paid: ["Paid", "Accepted"].includes(booking.status), accepted: booking.status === "Accepted" };
  const ended = booking.status === "Cancelled" || booking.status === "Expired";

  return (
    <>
      <Link to="/bookings" className="back">← My bookings</Link>
      <div className="card">
        <div className="detail-head">
          <div>
            <h1>{booking.pilgrimName}</h1>
            <StatusBadge status={booking.status} />
          </div>
          <span className="price big">{money(booking)}</span>
        </div>

        <ol className="timeline">
          {/* For cancelled/expired bookings only the current status is stored, so we don't guess
              whether it had been paid; we show "created -> ended" (refunds show in the cancel message). */}
          {steps.filter((s) => !ended || s.key === "created").map((s) => (
            <li key={s.key} className={reached[s.key] ? "done" : ""}>{s.label}</li>
          ))}
          {ended && <li className="ended">{booking.status === "Cancelled" ? "Cancelled" : "Expired – not paid within 30 minutes"}</li>}
        </ol>

        <dl className="facts">
          <dt>Booking id</dt><dd className="mono">{booking.id}</dd>
          <dt>Created</dt><dd>{formatDate(booking.createdAtUtc)}</dd>
          <dt>Pay before</dt><dd>{formatDate(booking.paymentDeadlineUtc)} {booking.status === "PendingPayment" && <Countdown deadline={booking.paymentDeadlineUtc} />}</dd>
          <dt>Last update</dt><dd>{formatDate(booking.updatedAtUtc)}</dd>
        </dl>

        <div className="row">
          {canPay(booking) && <button className="btn btn-primary" disabled={busyId === booking.id} onClick={() => pay(booking)}>Pay {money(booking)}</button>}
          {canCancel(booking) && <button className="btn btn-danger" disabled={busyId === booking.id} onClick={() => cancel(booking)}>Cancel booking</button>}
        </div>
        {booking.status === "Paid" && <p className="muted small">You can cancel for a full refund until a performer accepts.</p>}
      </div>
    </>
  );
}
