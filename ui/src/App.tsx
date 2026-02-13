import { useEffect, useMemo, useState } from "react";
import { Navigate, NavLink, Outlet, Route, Routes, useLocation, type Location } from "react-router-dom";
import SwipeDeckPage from "./pages/SwipeDeckPage";
import LoginPage from "./pages/LoginPage";
import PreferencesPage from "./pages/PreferencesPage";
import MyLikedMoviesPage from "./pages/MyLikedMoviesPage";
import MatchListPage from "./pages/MatchListPage";
import AdminConsolePage from "./pages/AdminConsolePage";
import ServiceSelectPage from "./pages/ServiceSelectPage";
import NotFoundPage from "./pages/NotFoundPage";
import { useAuth } from "./auth/AuthContext";
import TmdbBulkJobToast from "./components/TmdbBulkJobToast";

const HEADER_COLLAPSED_KEY = "tindarr:headerCollapsed:v1";

export default function App() {
  const location = useLocation();
  const state = location.state as { backgroundLocation?: Location } | null;
  const backgroundLocation = state?.backgroundLocation;

  return (
    <>
      <Routes location={backgroundLocation ?? location}>
        <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
          <Route index element={<ServiceSelectPage />} />
          <Route path="/swipe" element={<SwipeDeckPage />} />
          <Route path="/preferences" element={<PreferencesPage />} />
          <Route path="/liked" element={<MyLikedMoviesPage />} />
          <Route path="/matches" element={<MatchListPage />} />
          <Route
            path="/admin"
            element={
              <RequireRole role="Admin">
                <AdminConsolePage />
              </RequireRole>
            }
          />
        </Route>

        <Route path="/login" element={<LoginPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>

      {backgroundLocation ? (
        <Routes>
          <Route path="/preferences" element={<PreferencesPage />} />
          <Route path="/liked" element={<MyLikedMoviesPage />} />
          <Route path="/matches" element={<MatchListPage />} />
        </Routes>
      ) : null}
    </>
  );
}

function AppLayout() {
  const { user, logout } = useAuth();
  const location = useLocation();
  const isAdmin = user?.roles?.includes("Admin") ?? false;

  const [headerCollapsed, setHeaderCollapsed] = useState<boolean>(() => {
    try {
      return localStorage.getItem(HEADER_COLLAPSED_KEY) === "1";
    } catch {
      return false;
    }
  });

  useEffect(() => {
    try {
      localStorage.setItem(HEADER_COLLAPSED_KEY, headerCollapsed ? "1" : "0");
    } catch {
      // ignore
    }
  }, [headerCollapsed]);

  const headerToggleLabel = useMemo(() => (headerCollapsed ? "Expand" : "Collapse"), [headerCollapsed]);

  return (
    <div className={`app ${headerCollapsed ? "app--headerCollapsed" : ""}`}>
      <header className="app__header">
        <div className="app__headerInner">
          <div className="app__brand">
            <h1>Tindarr</h1>
          </div>
          <nav className="app__nav" aria-label="Primary">
            <button
              type="button"
              className="app__navLink"
              onClick={() => setHeaderCollapsed((prev) => !prev)}
              aria-pressed={headerCollapsed}
              title={headerCollapsed ? "Expand header" : "Collapse header"}
            >
              {headerToggleLabel}
            </button>

            {!headerCollapsed ? (
              <>
                <NavLink to="/swipe" className={({ isActive }) => `app__navLink ${isActive ? "is-active" : ""}`}>
                  Swipe
                </NavLink>
                <NavLink
                  to="/liked"
                  state={{ backgroundLocation: location }}
                  className={({ isActive }) => `app__navLink ${isActive ? "is-active" : ""}`}
                >
                  My Likes
                </NavLink>
                <NavLink
                  to="/matches"
                  state={{ backgroundLocation: location }}
                  className={({ isActive }) => `app__navLink ${isActive ? "is-active" : ""}`}
                >
                  Matches
                </NavLink>
                <NavLink
                  to="/preferences"
                  state={{ backgroundLocation: location }}
                  className={({ isActive }) => `app__navLink ${isActive ? "is-active" : ""}`}
                >
                  Preferences
                </NavLink>
                {isAdmin ? (
                  <NavLink to="/admin" className={({ isActive }) => `app__navLink ${isActive ? "is-active" : ""}`}>
                    Admin
                  </NavLink>
                ) : null}
                <span className="app__navUser">{user?.displayName ?? user?.userId}</span>
                <button type="button" className="app__navLink" onClick={logout}>
                  Logout
                </button>
              </>
            ) : null}
          </nav>
        </div>
      </header>
      <main className="app__content">
        <Outlet />
      </main>

      <TmdbBulkJobToast />
    </div>
  );
}

function RequireRole({ role, children }: { role: string; children: React.ReactNode }) {
  const { user } = useAuth();

  if (!user) {
    return <Navigate to="/login" replace />;
  }

  if (!user.roles.includes(role)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth();
  const location = useLocation();

  if (loading) {
    return (
      <div className="app">
        <main className="app__content">
          <section className="deck">
            <div className="deck__state">Loading…</div>
          </section>
        </main>
      </div>
    );
  }

  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  return <>{children}</>;
}
