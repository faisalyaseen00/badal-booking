import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { useAuth } from "../auth/AuthContext";
import type { Role } from "../api/client";

// Route guard for the UI. The API enforces roles on every request anyway (401/403);
// this only keeps people from landing on pages they can't use.
export function RequireRole({ role, children }: { role: Role; children: ReactNode }) {
  const { session, loading } = useAuth();
  const location = useLocation();

  if (loading) return <p className="muted">Loading…</p>;
  if (!session) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  if (session.user.role !== role) return <Navigate to={homeFor(session.user.role)} replace />;
  return <>{children}</>;
}

export const homeFor = (role: Role) => (role === "Performer" ? "/performer" : role === "Admin" ? "/admin" : "/bookings");
