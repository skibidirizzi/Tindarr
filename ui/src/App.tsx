import { Navigate, NavLink, Outlet, Route, Routes, useLocation, type Location } from "react-router-dom";
import SwipeDeckPage from "./pages/SwipeDeckPage";
import LoginPage from "./pages/LoginPage";
import PreferencesPage from "./pages/PreferencesPage";
import MyLikedMoviesPage from "./pages/MyLikedMoviesPage";
import MatchListPage from "./pages/MatchListPage";
import NotFoundPage from "./pages/NotFoundPage";
import { useAuth } from "./auth/AuthContext";

export default function App() {
  const location = useLocation();
  const state = location.state as { backgroundLocation?: Location } | null;
  const backgroundLocation = state?.backgroundLocation;

  return (
    <>
      <Routes location={backgroundLocation ?? location}>
        <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
          <Route index element={<Navigate to="/swipe" replace />} />
          <Route path="/swipe" element={<SwipeDeckPage />} />
          <Route path="/preferences" element={<PreferencesPage />} />
          <Route path="/liked" element={<MyLikedMoviesPage />} />
          <Route path="/matches" element={<MatchListPage />} />
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
  return (
    <div className="app">
      <header className="app__header">
        <div className="app__headerInner">
          <div className="app__brand">
            <h1>Tindarr</h1>
            <p>Swipe to like, skip, or superlike.</p>
          </div>
          <nav className="app__nav">
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
            <span className="app__navUser">{user?.displayName ?? user?.userId}</span>
            <button type="button" className="app__navLink" onClick={logout}>
              Logout
            </button>
          </nav>
        </div>
      </header>
      <main className="app__content">
        <Outlet />
      </main>
    </div>
  );
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
