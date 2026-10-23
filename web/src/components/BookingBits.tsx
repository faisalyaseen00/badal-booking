import { useEffect, useState } from "react";
import { statusLabel, type Booking, type BookingStatus } from "../api/bookings";

export function StatusBadge({ status }: { status: BookingStatus }) {
  return <span className={`status status-${status}`}>{statusLabel[status]}</span>;
}

// "Pay within 12:34" countdown for unpaid bookings. The server enforces the real deadline;
// this is only a helpful display.
export function Countdown({ deadline }: { deadline: string }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);
  const ms = Date.parse(deadline) - now;
  if (ms <= 0) return <span className="countdown late">Payment window closed</span>;
  const m = Math.floor(ms / 60000), s = Math.floor((ms % 60000) / 1000);
  return <span className="countdown">Pay within {m}:{String(s).padStart(2, "0")}</span>;
}

export const formatDate = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

export const money = (b: Pick<Booking, "amount" | "currency">) =>
  `${b.amount.toLocaleString(undefined, { minimumFractionDigits: 2 })} ${b.currency}`;

export const canPay = (b: Booking) => b.status === "PendingPayment" && Date.parse(b.paymentDeadlineUtc) > Date.now();
export const canCancel = (b: Booking) => b.status === "PendingPayment" || b.status === "Paid";
