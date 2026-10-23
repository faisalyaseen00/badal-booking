import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { applicationsApi, type PerformerApplication } from "../api/applications";
import { ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { formatDate } from "../components/BookingBits";
import { useToast } from "../components/Toasts";

// Customers apply here. An admin reviews the application; nothing changes until it's approved.
export function BecomePerformer() {
  const { session, refreshSession, logout } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const [application, setApplication] = useState<PerformerApplication | null | undefined>(undefined);
  const [form, setForm] = useState({ fullName: "", phone: "", note: "" });
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<Record<string, string[]>>({});

  const load = useCallback(async () => {
    if (!session) return;
    try { setApplication((await applicationsApi.mine(session.token)) ?? null); }
    catch { /* keep the last known state; the next refresh retries */ }
  }, [session]);

  useEffect(() => { void load(); const t = setInterval(load, 15_000); return () => clearInterval(t); }, [load]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!session) return;
    setBusy(true); setErrors({});
    try {
      setApplication(await applicationsApi.apply(session.token, form));
      toast("ok", "Application sent", "An administrator will review it.");
    } catch (err) {
      if (err instanceof ApiError) setErrors(err.fieldErrors);
      toast("error", "Couldn't send application", err instanceof Error ? err.message : undefined);
    } finally {
      setBusy(false);
    }
  }

  // After approval the current API token still says "Customer". Getting a fresh one picks up the
  // new role from the database (customers who signed in with a password must log in again).
  async function switchToPerformer() {
    setBusy(true);
    try {
      if (session?.method === "firebase") { await refreshSession(); navigate("/performer"); }
      else { await logout(); navigate("/login"); }
    } finally { setBusy(false); }
  }

  if (application === undefined) return <p className="muted">Loading…</p>;

  const showForm = application === null || application.status === "Rejected";

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Become a performer</h1>
          <p className="muted">Performers perform Umrah on behalf of customers. Every application is reviewed by an administrator.</p>
        </div>
      </div>

      {application && (
        <div className={`card application application-${application.status}`}>
          <h2>Your application: {application.status === "Pending" ? "waiting for review" : application.status.toLowerCase()}</h2>
          <dl className="facts">
            <dt>Submitted</dt><dd>{formatDate(application.createdAtUtc)}</dd>
            <dt>Name</dt><dd>{application.fullName}</dd>
            <dt>Phone</dt><dd>{application.phone}</dd>
            {application.decidedAtUtc && <><dt>Decided</dt><dd>{formatDate(application.decidedAtUtc)}</dd></>}
            {application.decisionNote && <><dt>Admin note</dt><dd>{application.decisionNote}</dd></>}
          </dl>
          {application.status === "Pending" && <p className="muted small">This page checks for a decision automatically.</p>}
          {application.status === "Approved" && (
            <button className="btn btn-primary" disabled={busy} onClick={switchToPerformer}>Switch to performer view</button>
          )}
          {application.status === "Rejected" && <p className="muted small">You can update your details and apply again below.</p>}
        </div>
      )}

      {showForm && (
        <form className="card form" onSubmit={submit}>
          <h2>{application ? "Apply again" : "Apply"}</h2>
          <label>Full name<input required maxLength={200} value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} /></label>
          {errors.fullName && <p className="alert alert-error">{errors.fullName[0]}</p>}
          <label>Phone<input required maxLength={30} inputMode="tel" placeholder="+966 50 123 4567" value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} /></label>
          {errors.phone && <p className="alert alert-error">{errors.phone[0]}</p>}
          <label>About you (optional)<textarea maxLength={1000} rows={4} placeholder="Where you live, your experience performing Umrah…" value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} /></label>
          <button className="btn btn-primary" disabled={busy}>{busy ? "Sending…" : "Send application"}</button>
        </form>
      )}
    </>
  );
}
