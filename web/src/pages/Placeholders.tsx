import { Link } from "react-router";

export function NotFound() {
  return (
    <div className="card narrow">
      <h1>Page not found</h1>
      <Link to="/" className="btn">Go home</Link>
    </div>
  );
}
