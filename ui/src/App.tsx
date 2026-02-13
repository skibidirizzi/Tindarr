import { useEffect, useMemo, useState } from "react";
import { Navigate, NavLink, Outlet, Route, Routes, useLocation, type Location } from "react-router-dom";
import SwipeDeckPage from "./pages/SwipeDeckPage";
import LoginPage from "./pages/LoginPage";
import PreferencesPage from "./pages/PreferencesPage";
import MyLikedMoviesPage from "./pages/MyLikedMoviesPage";
import MatchListPage from "./pages/MatchListPage";
import AdminConsolePage from "./pages/AdminConsolePage";
import ServiceSelectPage from "./pages/ServiceSelectPage";
import RoomPage from "./pages/RoomPage";
import NotFoundPage from "./pages/NotFoundPage";
import { useAuth } from "./auth/AuthContext";
import TmdbBulkJobToast from "./components/TmdbBulkJobToast";
import { fetchConfiguredScopes } from "./api/client";
import type { ServiceScopeOptionDto } from "./api/contracts";
import { getServiceScope, SERVICE_SCOPE_UPDATED_EVENT, setServiceScopeAndNotify, type ServiceScope } from "./serviceScope";

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
          <Route path="/rooms" element={<RoomPage />} />
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

        {/* Invite link join route (supports guest join) */}
        <Route path="/rooms/:roomId" element={<RoomPage />} />

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

  const [currentScope, setCurrentScope] = useState<ServiceScope>(() => getServiceScope());
  const [availableScopes, setAvailableScopes] = useState<ServiceScopeOptionDto[]>([]);

  const [headerCollapsed, setHeaderCollapsed] = useState<boolean>(() => {
    try {
      return localStorage.getItem(HEADER_COLLAPSED_KEY) === "1";
    } catch {
      return false;
    }
  });

  useEffect(() => {
    fetchConfiguredScopes()
      .then(setAvailableScopes)
      .catch((err) => console.error("Failed to fetch configured scopes:", err));
  }, []);

  useEffect(() => {
    try {
      localStorage.setItem(HEADER_COLLAPSED_KEY, headerCollapsed ? "1" : "0");
    } catch {
      // ignore
    }
  }, [headerCollapsed]);

  useEffect(() => {
    function handleScopeUpdated() {
      setCurrentScope(getServiceScope());
    }

    window.addEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
    return () => window.removeEventListener(SERVICE_SCOPE_UPDATED_EVENT, handleScopeUpdated);
  }, []);

  const headerToggleLabel = useMemo(() => (headerCollapsed ? "Expand" : "Collapse"), [headerCollapsed]);

  const scopeOptions = useMemo(() => {
    // Map API scope options to dropdown format
    const mapped = availableScopes.map((o) => ({
      value: `${o.serviceType}/${o.serverId}`,
      label: o.displayName,
      scope: { serviceType: o.serviceType as any, serverId: o.serverId }
    }));

    // Ensure current scope is included even if not in the API list (fallback for edge cases)
    const currentValue = `${currentScope.serviceType}/${currentScope.serverId}`;
    if (!mapped.some((o) => o.value === currentValue)) {
      mapped.push({
        value: currentValue,
        label: `${currentScope.serviceType} (${currentScope.serverId})`,
        scope: currentScope
      });
    }

    return mapped;
  }, [availableScopes, currentScope]);

  const selectedScopeValue = useMemo(() => `${currentScope.serviceType}/${currentScope.serverId}`, [currentScope]);

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

      <select
        className="app__navLink"
        value={selectedScopeValue}
        onChange={(e) => {
          const value = e.target.value;
          const option = scopeOptions.find((o) => o.value === value);
          if (option) {
            setServiceScopeAndNotify(option.scope);
          }
        }}
        aria-label="Service scope"
        title="Service scope"
      >
        {scopeOptions.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>

            {!headerCollapsed ? (
              <>
                <NavLink to="/swipe" className={({ isActive }) => `app__navLink ${isActive ? "is-active" : ""}`}>
                  Swipe
                </NavLink>
                <NavLink to="/rooms" className={({ isActive }) => `app__navLink ${isActive ? "is-active" : ""}`}>
                  Rooms
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
