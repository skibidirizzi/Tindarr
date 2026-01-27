import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ApiError } from "../api/http";
import { useAuth } from "../auth/AuthContext";
import { getStoredUserId } from "../auth/session";

type Mode = "login" | "register";

export default function LoginPage() {
  const { login, register } = useAuth();
  const navigate = useNavigate();

  const [mode, setMode] = useState<Mode>("login");
  const [userId, setUserId] = useState(getStoredUserId() ?? "");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const title = useMemo(() => (mode === "login" ? "Login" : "Create account"), [mode]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      if (mode === "login") {
        await login(userId.trim(), password);
      } else {
        await register(userId.trim(), displayName.trim(), password);
      }
      navigate("/swipe", { replace: true });
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else if (err instanceof Error) {
        setError(err.message);
      } else {
        setError("Request failed.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="deck">
      <div className="deck__state">
        <h2 style={{ marginTop: 0 }}>{title}</h2>
        <form className="form" onSubmit={handleSubmit}>
          <label className="field">
            <span className="field__label">User ID</span>
            <input
              className="input"
              value={userId}
              onChange={(e) => setUserId(e.target.value)}
              autoComplete="username"
              required
            />
          </label>

          {mode === "register" ? (
            <label className="field">
              <span className="field__label">Display name</span>
              <input
                className="input"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                autoComplete="nickname"
                required
              />
            </label>
          ) : null}

          <label className="field">
            <span className="field__label">Password</span>
            <input
              className="input"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete={mode === "login" ? "current-password" : "new-password"}
              required
            />
          </label>

          {error ? <div className="deck__state deck__state--error">{error}</div> : null}

          <div className="form__actions">
            <button className="button button--like" type="submit" disabled={busy}>
              {busy ? "Working…" : mode === "login" ? "Login" : "Register"}
            </button>
            <button
              className="button button--ghost"
              type="button"
              onClick={() => {
                setError(null);
                setMode((m) => (m === "login" ? "register" : "login"));
              }}
              disabled={busy}
            >
              {mode === "login" ? "Need an account?" : "Have an account?"}
            </button>
          </div>
        </form>
      </div>
    </section>
  );
}

