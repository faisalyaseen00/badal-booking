import { useCallback, useRef, useState } from "react";
import { bookingsApi, type Booking } from "../api/bookings";
import { ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useToast } from "../components/Toasts";

// Pay and cancel, shared by the list and the detail page.
export function useBookingActions(onChanged: () => void) {
  const { session } = useAuth();
  const toast = useToast();
  const [busyId, setBusyId] = useState<string | null>(null);
  // One idempotency key per booking until the payment succeeds or is refused:
  // retrying after a network error reuses it, so the customer is never charged twice.
  const payKeys = useRef(new Map<string, string>());

  const pay = useCallback(async (b: Booking) => {
    if (!session) return;
    const key = payKeys.current.get(b.id) ?? crypto.randomUUID();
    payKeys.current.set(b.id, key);
    setBusyId(b.id);
    try {
      const payment = await bookingsApi.pay(session.token, b.id, key);
      payKeys.current.delete(b.id);
      toast("ok", "Payment successful", `${payment.amount.toFixed(2)} ${payment.currency}${payment.simulated ? " (simulated)" : ""}`);
    } catch (e) {
      if (e instanceof ApiError && e.status !== 0) payKeys.current.delete(b.id); // final answer from the server
      toast("error", "Payment failed", e instanceof Error ? e.message : undefined);
    } finally {
      setBusyId(null);
      onChanged();
    }
  }, [session, toast, onChanged]);

  const cancel = useCallback(async (b: Booking) => {
    if (!session) return;
    const refundNote = b.status === "Paid" ? " Your payment will be refunded." : "";
    if (!window.confirm(`Cancel the booking for ${b.pilgrimName}?${refundNote}`)) return;
    setBusyId(b.id);
    try {
      const result = await bookingsApi.cancel(session.token, b.id);
      toast("ok", "Booking cancelled", result.refunded ? "The payment was refunded (simulated)." : undefined);
    } catch (e) {
      toast("error", "Couldn't cancel", e instanceof Error ? e.message : undefined);
    } finally {
      setBusyId(null);
      onChanged();
    }
  }, [session, toast, onChanged]);

  return { pay, cancel, busyId };
}
