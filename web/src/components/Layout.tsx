import { NavLink, Outlet, useNavigate } from "react-router";
import { useAuth } from "../auth/AuthContext";

export function Layout() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();
  const role = session?.user.role;

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar-inner">
          <NavLink to="/" className="brand">
            <span className="brand-mark">B</span> Badal Booking
          </NavLink>
          <nav className="nav">
            {role === "Customer" && <NavLink to="/bookings">My bookings</NavLink>}
            {role === "Performer" && <NavLink to="/performer">Available bookings</NavLink>}
            <NavLink to="/status">Status</NavLink>
          </nav>
          <div className="user">
            {session ? (
              <>
                <span className="user-email">{session.user.email}</span>
                <span className={`badge badge-${role?.toLowerCase()}`}>{role}</span>
                <button className="btn btn-ghost" onClick={async () => { await logout(); navigate("/login"); }}>
                  Log out
                </button>
              </>
            ) : (
              <NavLink to="/login" className="btn btn-primary">Sign in</NavLink>
            )}
          </div>
        </div>
      </header>
      <main className="container">
        <Outlet />
      </main>
      <footer className="footer">Umrah by proxy · fixed price 500 SAR · payments are simulated in this demo</footer>
    </div>
  );
}
