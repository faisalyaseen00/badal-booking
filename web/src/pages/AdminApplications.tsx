import { useCallback, useEffect, useState } from "react";
import { applicationsApi, type AdminApplication, type ApplicationStatus } from "../api/applications";
import { ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { formatDate } from "../components/BookingBits";
import { useToast } from "../components/Toasts";

const tabs: ApplicationStatus[] = ["Pending", "Approved", "Rejected"];

// Admin only (the API returns 403 to everyone else).
export function AdminApplications() {
  const { session } = useAuth();
  const toast = useToast();
  const [tab, setTab] = useState<ApplicationStatus>("Pending");
  const [rows, setRows] = useState<AdminApplication[] | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!session) return;
    try { setRows(await applicationsApi.list(session.token, tab)); }
    catch (e) { toast("error", "Couldn't load applications", e instanceof Error ? e.message : undefined); }
  }, [session, tab, toast]);

  useEffect(() => { setRows(null); void load(); }, [load]);

  async function decide(row: AdminApplication, approve: boolean) {
    if (!session) return;
    const name = row.application.fullName;
    const note = window.prompt(approve ? `Approve ${name}? Optional note:` : `Reject ${name}? Reason (shown to the applicant):`, "");
    if (note === null) return; // cancelled
    setBusyId(row.application.id);
    try {
      if (approve) await applicationsApi.approve(session.token, row.application.id, note || undefined);
      else await applicationsApi.reject(session.token, row.application.id, note || undefined);
      toast("ok", approve ? "Approved" : "Rejected", approve ? `${name} is now a performer.` : undefined);
    } catch (e) {
      const text = e instanceof ApiError && e.status === 409 ? "Another admin already decided this application." : e instanceof Error ? e.message : undefined;
      toast("error", "Couldn't save decision", text);
    } finally {
      setBusyId(null);
      await load();
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Performer applications</h1>
          <p className="muted">Approving changes the user's role to Performer. They get access after signing in again.</p>
        </div>
      </div>

      <div className="tabs tabs-3">
        {tabs.map((t) => (
          <button key={t} className={t === tab ? "tab active" : "tab"} onClick={() => setTab(t)}>{t}</button>
        ))}
      </div>

      {rows === null && <p className="muted">Loading…</p>}
      {rows?.length === 0 && <div className="card empty">No {tab.toLowerCase()} applications.</div>}

      <ul className="booking-list">
        {rows?.map((r) => (
          <li key={r.application.id} className="card booking">
            <div className="booking-main">
              <span className="booking-name">{r.application.fullName}</span>
              <div className="booking-meta">
                <span className="muted small">{r.email}</span>
                <span className="muted small">{r.application.phone}</span>
                <span className="muted small">Applied {formatDate(r.application.createdAtUtc)}</span>
              </div>
              {r.application.note && <p className="note">{r.application.note}</p>}
              {r.application.decisionNote && <p className="muted small">Admin note: {r.application.decisionNote}</p>}
            </div>
            {tab === "Pending" && (
              <div className="row">
                <button className="btn btn-primary" disabled={busyId === r.application.id} onClick={() => decide(r, true)}>Approve</button>
                <button className="btn btn-danger" disabled={busyId === r.application.id} onClick={() => decide(r, false)}>Reject</button>
              </div>
            )}
          </li>
        ))}
      </ul>
    </>
  );
}
