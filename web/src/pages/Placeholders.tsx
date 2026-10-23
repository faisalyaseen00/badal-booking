import { Link } from "react-router";

// Screens filled in by later steps. Kept tiny so step 2.7 is only the foundation.
export function StatusPage() {
  return (
    <div className="card">
      <h1>System status</h1>
      <p className="muted">Health check and the demo customer's latest bookings arrive in step 3.5.</p>
    </div>
  );
}

export function NotFound() {
  return (
    <div className="card narrow">
      <h1>Page not found</h1>
      <Link to="/" className="btn">Go home</Link>
    </div>
  );
}
