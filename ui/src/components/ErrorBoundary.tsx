import React from "react";

type Props = {
  children: React.ReactNode;
};

type State = {
  error: Error | null;
};

export default class ErrorBoundary extends React.Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    // eslint-disable-next-line no-console
    console.error("UI crashed:", error, info);
  }

  render() {
    if (!this.state.error) return this.props.children;

    return (
      <div className="app">
        <main className="app__content">
          <section className="deck">
            <div className="deck__state deck__state--error" style={{ textAlign: "left" }}>
              <h2 style={{ marginTop: 0 }}>Something went wrong</h2>
              <div style={{ fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace" }}>
                {this.state.error.name}: {this.state.error.message}
              </div>
            </div>
          </section>
        </main>
      </div>
    );
  }
}
