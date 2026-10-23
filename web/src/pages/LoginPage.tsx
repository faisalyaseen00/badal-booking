import { useEffect, useState, type FormEvent } from "react";
import { Navigate, useNavigate } from "react-router";
import { FirebaseError } from "firebase/app";
import { useAuth } from "../auth/AuthContext";
import { ApiError } from "../api/client";
import { homeFor } from "../components/RequireRole";

type Tab = "customer" | "performer";

// Friendly text for common Firebase/API errors.
function message(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof FirebaseError) {
    switch (error.code) {
      case "auth/invalid-credential":
      case "auth/wrong-password":
      case "auth/user-not-found": return "Wrong email or password.";
      case "auth/email-already-in-use": return "An account with this email already exists. Sign in instead.";
      case "auth/weak-password": return "Password must be at least 6 characters.";
      case "auth/invalid-email": return "Enter a valid email address.";
      case "auth/too-many-requests": return "Too many attempts. Wait a moment and try again.";
    }
  }
  return "Something went wrong. Please try again.";
}

export function LoginPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [tab, setTab] = useState<Tab>("customer");
  const [mode, setMode] = useState<"signin" | "signup">("signin");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const needsVerification = tab === "customer" && !!auth.firebaseUser && !auth.firebaseUser.emailVerified;

  useEffect(() => { setError(null); setInfo(null); }, [tab, mode]);

  if (auth.session) return <Navigate to={homeFor(auth.session.user.role)} replace />;

  async function attempt(action: () => Promise<void>) {
    setBusy(true); setError(null); setInfo(null);
    try { await action(); } catch (e) { setError(message(e)); } finally { setBusy(false); }
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    attempt(async () => {
      if (tab === "performer") await auth.signInPerformer(email, password);
      else if (mode === "signup") {
        await auth.signUpCustomer(email, password);
        setInfo("Account created. We sent you a verification email.");
      } else await auth.signInCustomer(email, password);
    });
  }

  if (needsVerification) {
    return (
      <div className="card narrow">
        <h1>Verify your email</h1>
        <p>We sent a link to <strong>{auth.firebaseUser!.email}</strong>. Open it, then come back here.</p>
        <p className="muted small">A verified email is required so nobody can sign in with an address they don't own.</p>
        {error && <p className="alert alert-error">{error}</p>}
        {info && <p className="alert alert-ok">{info}</p>}
        <div className="row">
          <button className="btn btn-primary" disabled={busy} onClick={() => attempt(async () => {
            if (await auth.checkVerified()) navigate("/bookings");
            else setError("Not verified yet. Click the link in the email first.");
          })}>I've verified my email</button>
          <button className="btn" disabled={busy} onClick={() => attempt(async () => {
            await auth.resendVerification(); setInfo("Verification email sent again.");
          })}>Resend email</button>
          <button className="btn btn-ghost" onClick={() => auth.logout()}>Use another account</button>
        </div>
      </div>
    );
  }

  return (
    <div className="card narrow">
      <div className="tabs">
        <button className={tab === "customer" ? "tab active" : "tab"} onClick={() => setTab("customer")}>Customer</button>
        <button className={tab === "performer" ? "tab active" : "tab"} onClick={() => setTab("performer")}>Performer</button>
      </div>

      <h1>{tab === "performer" ? "Performer sign in" : mode === "signup" ? "Create your account" : "Sign in"}</h1>
      <p className="muted">
        {tab === "performer"
          ? "Performer accounts are created by the administrator."
          : "Book an Umrah performed on behalf of a loved one. Fixed price: 500 SAR."}
      </p>

      <form onSubmit={submit} className="form">
        <label>Email<input type="email" required autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
        <label>Password<input type="password" required minLength={tab === "customer" && mode === "signup" ? 6 : 1}
          autoComplete={mode === "signup" ? "new-password" : "current-password"} value={password} onChange={(e) => setPassword(e.target.value)} /></label>
        {error && <p className="alert alert-error">{error}</p>}
        {info && <p className="alert alert-ok">{info}</p>}
        <button className="btn btn-primary btn-block" disabled={busy}>
          {busy ? "Please wait…" : tab === "performer" ? "Sign in as performer" : mode === "signup" ? "Create account" : "Sign in"}
        </button>
      </form>

      {tab === "customer" && (
        <p className="small center">
          {mode === "signin" ? "New here? " : "Already have an account? "}
          <button className="link" onClick={() => setMode(mode === "signin" ? "signup" : "signin")}>
            {mode === "signin" ? "Create an account" : "Sign in"}
          </button>
        </p>
      )}
    </div>
  );
}
