import { Component, type ErrorInfo, type ReactNode } from 'react';

type Props = {
  children: ReactNode;
  /** "page" fills the screen (last line of defence); "section" keeps the rest of the page usable. */
  variant?: 'page' | 'section';
  title?: string;
};
type State = { error: Error | null };

/**
 * Catches rendering crashes (for example a chart receiving unexpected data) so one broken part shows a
 * friendly message instead of blanking the whole app. Give it a `key` that changes on navigation to reset it.
 */
export default class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Kept in the browser console for debugging; nothing sensitive is sent anywhere.
    console.error('DollarShark UI error', error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    const isPage = this.props.variant === 'page';
    return <div className={isPage ? 'error-fallback error-fallback-page' : 'error-fallback content-card p-4 mb-4'} role="alert">
      <div className="error-fallback-icon" aria-hidden="true">!</div>
      <h2 className="h5 fw-bold mb-1">{this.props.title ?? (isPage ? 'Something went wrong' : 'This section could not be displayed')}</h2>
      <p className="text-secondary small mb-3">Your data is safe. Try again, or reload the page if it keeps happening.</p>
      <div className="d-flex gap-2 justify-content-center">
        {!isPage && <button type="button" className="btn btn-sm btn-outline-dark" onClick={() => this.setState({ error: null })}>Try again</button>}
        <button type="button" className="btn btn-sm btn-dark" onClick={() => window.location.reload()}>Reload page</button>
      </div>
    </div>;
  }
}
