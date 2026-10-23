import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { Layout } from "./components/Layout";
import { RequireRole, homeFor } from "./components/RequireRole";
import { LoginPage } from "./pages/LoginPage";
import { NotFound } from "./pages/Placeholders";
import { StatusPage } from "./pages/StatusPage";
import { PerformerHome } from "./pages/PerformerHome";
import { BecomePerformer } from "./pages/BecomePerformer";
import { AdminApplications } from "./pages/AdminApplications";
import { CustomerBookings } from "./pages/CustomerBookings";
import { BookingDetail } from "./pages/BookingDetail";
import { ToastProvider } from "./components/Toasts";
import { PushProvider } from "./push/PushProvider";

function Home() {
  const { session, loading } = useAuth();
  if (loading) return <p className="muted">Loading…</p>;
  return <Navigate to={session ? homeFor(session.user.role) : "/login"} replace />;
}

export function App() {
  return (
    <ToastProvider>
    <AuthProvider>
    <PushProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<Home />} />
            <Route path="login" element={<LoginPage />} />
            <Route path="bookings" element={<RequireRole role="Customer"><CustomerBookings /></RequireRole>} />
            <Route path="bookings/:id" element={<RequireRole role="Customer"><BookingDetail /></RequireRole>} />
            <Route path="performer" element={<RequireRole role="Performer"><PerformerHome /></RequireRole>} />
            <Route path="become-performer" element={<RequireRole role="Customer"><BecomePerformer /></RequireRole>} />
            <Route path="admin" element={<RequireRole role="Admin"><AdminApplications /></RequireRole>} />
            <Route path="status" element={<StatusPage />} />
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </PushProvider>
    </AuthProvider>
    </ToastProvider>
  );
}
