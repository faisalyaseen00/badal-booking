import { apiFetch } from "./client";

export type ApplicationStatus = "Pending" | "Approved" | "Rejected";

export interface PerformerApplication {
  id: string;
  fullName: string;
  phone: string;
  note: string | null;
  status: ApplicationStatus;
  createdAtUtc: string;
  decidedAtUtc: string | null;
  decisionNote: string | null;
}

export interface AdminApplication { application: PerformerApplication; userId: string; email: string; currentRole: string }

export const applicationsApi = {
  apply: (token: string, body: { fullName: string; phone: string; note: string }) =>
    apiFetch<PerformerApplication>("/performer-applications", { method: "POST", token, body }),
  // undefined (204 No Content) = never applied
  mine: (token: string) => apiFetch<PerformerApplication | undefined>("/performer-applications/me", { token }),
  list: (token: string, status: ApplicationStatus) =>
    apiFetch<AdminApplication[]>(`/admin/performer-applications?status=${status}`, { token }),
  approve: (token: string, id: string, note?: string) =>
    apiFetch(`/admin/performer-applications/${id}/approve`, { method: "POST", token, body: { note } }),
  reject: (token: string, id: string, note?: string) =>
    apiFetch(`/admin/performer-applications/${id}/reject`, { method: "POST", token, body: { note } }),
};
