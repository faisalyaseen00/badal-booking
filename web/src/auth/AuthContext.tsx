import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import {
  createUserWithEmailAndPassword, onAuthStateChanged, sendEmailVerification,
  signInWithEmailAndPassword, signOut, type User as FirebaseUser,
} from "firebase/auth";
import { apiFetch, type ApiUser, type AuthResponse } from "../api/client";
import { firebaseAuth } from "../firebase";

// Two ways in, one kind of session:
//  - Customers: Firebase sign-in -> Firebase ID token -> POST /auth/firebase -> API JWT
//  - Performers: email + password -> POST /auth/login -> API JWT (performers can't use Firebase)
// The API JWT is what every other request uses.
export interface Session { token: string; expiresAt: number; user: ApiUser; method: "firebase" | "password" }

interface AuthState {
  session: Session | null;
  loading: boolean;
  firebaseUser: FirebaseUser | null;        // signed in to Firebase (maybe not verified yet)
  signInCustomer(email: string, password: string): Promise<void>;
  signUpCustomer(email: string, password: string): Promise<void>;
  resendVerification(): Promise<void>;
  checkVerified(): Promise<boolean>;
  signInPerformer(email: string, password: string): Promise<void>;
  // Get a fresh API token (e.g. after an admin approved you: the new token carries the new role).
  refreshSession(): Promise<void>;
  logout(): Promise<void>;
  setBeforeLogout(fn: (() => Promise<void>) | null): void;
}

const AuthContext = createContext<AuthState | null>(null);

// Performer sessions survive a reload in sessionStorage (cleared when the tab closes).
// Customer sessions don't need storage: Firebase restores its sign-in and we exchange again.
const STORAGE_KEY = "badal.performerSession";

function toSession(r: AuthResponse, method: Session["method"]): Session {
  return { token: r.accessToken, expiresAt: Date.parse(r.expiresAtUtc), user: r.user, method };
}

function readStoredSession(): Session | null {
  try {
    const s = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? "null") as Session | null;
    return s && s.expiresAt > Date.now() + 30_000 ? s : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [firebaseUser, setFirebaseUser] = useState<FirebaseUser | null>(null);
  const [loading, setLoading] = useState(true);
  // A ref, not state: changing it must NOT re-render the app. (Storing it as state caused an
  // infinite render loop, found in the step 2.8 browser check.)
  const beforeLogout = useRef<(() => Promise<void>) | null>(null);
  const setBeforeLogout = useCallback((fn: (() => Promise<void>) | null) => { beforeLogout.current = fn; }, []);

  const exchange = useCallback(async (user: FirebaseUser, forceRefresh = false) => {
    const idToken = await user.getIdToken(forceRefresh);
    const result = await apiFetch<AuthResponse>("/auth/firebase", { method: "POST", body: { idToken } });
    setSession(toSession(result, "firebase"));
  }, []);

  // Restore a session after a page reload.
  useEffect(() => {
    return onAuthStateChanged(firebaseAuth, async (user) => {
      setFirebaseUser(user);
      try {
        if (user?.emailVerified) await exchange(user);
        else if (!user) setSession(readStoredSession());
      } catch {
        setSession(null);
      } finally {
        setLoading(false);
      }
    });
  }, [exchange]);

  // The API JWT lives 60 minutes. Customers silently get a new one; performers must log in again.
  useEffect(() => {
    if (!session) return;
    const ms = Math.max(session.expiresAt - Date.now() - 60_000, 0);
    const timer = setTimeout(async () => {
      if (session.method === "firebase" && firebaseAuth.currentUser) await exchange(firebaseAuth.currentUser, true).catch(() => setSession(null));
      else { sessionStorage.removeItem(STORAGE_KEY); setSession(null); }
    }, ms);
    return () => clearTimeout(timer);
  }, [session, exchange]);

  const value = useMemo<AuthState>(() => ({
    session, loading, firebaseUser,

    async signInCustomer(email, password) {
      const { user } = await signInWithEmailAndPassword(firebaseAuth, email, password);
      if (user.emailVerified) await exchange(user);
      // not verified: the login page shows the "verify your email" panel
    },

    async signUpCustomer(email, password) {
      const { user } = await createUserWithEmailAndPassword(firebaseAuth, email, password);
      await sendEmailVerification(user);
    },

    async resendVerification() {
      if (firebaseAuth.currentUser) await sendEmailVerification(firebaseAuth.currentUser);
    },

    async checkVerified() {
      const user = firebaseAuth.currentUser;
      if (!user) return false;
      await user.reload();
      if (!user.emailVerified) return false;
      await exchange(user, true); // new ID token now carries email_verified = true
      return true;
    },

    async signInPerformer(email, password) {
      const result = await apiFetch<AuthResponse>("/auth/login", { method: "POST", body: { email, password } });
      const s = toSession(result, "password");
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s));
      setSession(s);
    },

    async refreshSession() {
      if (session?.method === "firebase" && firebaseAuth.currentUser) await exchange(firebaseAuth.currentUser, true);
    },

    async logout() {
      try { await beforeLogout.current?.(); } catch { /* still log out locally */ }
      sessionStorage.removeItem(STORAGE_KEY);
      setSession(null);
      if (firebaseAuth.currentUser) await signOut(firebaseAuth);
    },

    setBeforeLogout,
  }), [session, loading, firebaseUser, exchange, setBeforeLogout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}
