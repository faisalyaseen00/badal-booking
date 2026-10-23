import { apiFetch } from "./client";

export type BookingStatus = "PendingPayment" | "Paid" | "Accepted" | "Cancelled" | "Expired";

export interface Booking {
  id: string;
  pilgrimName: string;
  amountHalalas: number;
  amount: number;
  currency: string;
  status: BookingStatus;
  createdAtUtc: string;
  paymentDeadlineUtc: string;
  updatedAtUtc: string;
}

export interface Payment {
  id: string;
  status: "Succeeded" | "Refunded";
  amount: number;
  currency: string;
  providerReference: string;
  simulated: boolean;
}

export const bookingsApi = {
  list: (token: string, limit = 50) => apiFetch<Booking[]>(`/bookings?limit=${limit}`, { token }),
  get: (token: string, id: string) => apiFetch<Booking>(`/bookings/${id}`, { token }),
  create: (token: string, pilgrimName: string) =>
    apiFetch<Booking>("/bookings", { method: "POST", token, body: { pilgrimName } }),
  // The key makes a retry (double click, flaky network) return the SAME payment instead of charging twice.
  pay: (token: string, id: string, idempotencyKey: string) =>
    apiFetch<Payment>(`/bookings/${id}/pay`, { method: "POST", token, headers: { "Idempotency-Key": idempotencyKey } }),
  cancel: (token: string, id: string) =>
    apiFetch<{ booking: Booking; refunded: boolean }>(`/bookings/${id}/cancel`, { method: "POST", token }),
};

export const statusLabel: Record<BookingStatus, string> = {
  PendingPayment: "Awaiting payment",
  Paid: "Paid – waiting for a performer",
  Accepted: "Accepted by a performer",
  Cancelled: "Cancelled",
  Expired: "Expired (not paid in time)",
};
