import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { Layout } from "./components/Layout";
import { RequireRole, homeFor } from "./components/RequireRole";
import { LoginPage } from "./pages/LoginPage";
import { CustomerHome, NotFound, PerformerHome, StatusPage } from "./pages/Placeholders";

function Home() {
  const { session, loading } = useAuth();
  if (loading) return <p className="muted">Loading…</p>;
  return <Navigate to={session ? homeFor(session.user.role) : "/login"} replace />;
}

export function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<Home />} />
            <Route path="login" element={<LoginPage />} />
            <Route path="bookings" element={<RequireRole role="Customer"><CustomerHome /></RequireRole>} />
            <Route path="performer" element={<RequireRole role="Performer"><PerformerHome /></RequireRole>} />
            <Route path="status" element={<StatusPage />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
