import SwipeDeckPage from "./pages/SwipeDeckPage";

export default function App() {
  return (
    <div className="app">
      <header className="app__header">
        <div>
          <h1>Tindarr</h1>
          <p>Swipe to like, skip, or superlike.</p>
        </div>
      </header>
      <main className="app__content">
        <SwipeDeckPage />
      </main>
    </div>
  );
}
