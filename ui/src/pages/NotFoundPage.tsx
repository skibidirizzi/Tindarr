import { Link } from "react-router-dom";

export default function NotFoundPage() {
  return (
    <section className="deck">
      <div className="deck__state deck__state--error">
        <h2 style={{ marginTop: 0 }}>Not found</h2>
        <p style={{ marginBottom: 0, color: "#c0c4d2" }}>
          That page doesn’t exist. Go back to <Link to="/swipe">Swipe</Link>.
        </p>
      </div>
    </section>
  );
}

