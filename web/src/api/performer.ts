import { apiFetch } from "./client";

// What performers see: only what's needed to perform the Umrah (no customer data).
export interface AvailableBooking { id: string; pilgrimName: string; createdAtUtc: string }
export interface AcceptedBooking { id: string; pilgrimName: string; createdAtUtc: string; acceptedAtUtc: string }

export const performerApi = {
  available: (token: string) => apiFetch<AvailableBooking[]>("/bookings/available", { token }),
  mine: (token: string) => apiFetch<AcceptedBooking[]>("/performer/bookings", { token }),
  accept: (token: string, id: string) => apiFetch(`/bookings/${id}/accept`, { method: "POST", token }),
};
