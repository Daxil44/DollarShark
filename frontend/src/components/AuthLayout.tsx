import type { ReactNode } from 'react';
import { Card, Container } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import DollarSharkLogo from './DollarSharkLogo';

/** Split-screen shell shared by every signed-out account page (sign in, sign up, reset, confirm). */
export default function AuthLayout({ asideTitle, children }: { asideTitle: string; children: ReactNode }) {
  return <main className="auth-split">
    <aside className="auth-aside" aria-hidden="true">
      <Link to="/" className="auth-aside-logo" tabIndex={-1}><DollarSharkLogo /></Link>
      <div className="auth-aside-copy">
        <h2>{asideTitle}</h2>
        <p>Import statements, catch duplicates, link transfers, and see where every dollar goes.</p>
      </div>
      <div className="auth-aside-art">
        <div className="auth-mini-card">
          <small>Net cash flow</small><strong>+$2,370.00</strong>
          <svg viewBox="0 0 200 60" preserveAspectRatio="none"><path d="M0 50 C30 46 50 48 70 38 S110 30 130 26 S170 14 200 8" /></svg>
        </div>
        <div className="auth-mini-card small-card"><span className="tick">✓</span>3 duplicates skipped</div>
      </div>
      <ul className="auth-aside-points"><li>No bank passwords</li><li>Private to your account</li><li>CSV preview before import</li></ul>
    </aside>
    <section className="auth-main"><Container className="auth-container">
      <Link to="/" className="auth-back">← Back to home</Link>
      <Card className="auth-card"><Card.Body>
        <Link to="/" className="brand mb-4 d-inline-flex" aria-label="DollarShark home"><DollarSharkLogo /></Link>
        {children}
      </Card.Body></Card>
    </Container></section>
  </main>;
}

export function MailIcon() {
  return <div className="auth-status-icon" aria-hidden="true">
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="5" width="18" height="14" rx="2" /><path d="m3 7 9 6 9-6" /></svg>
  </div>;
}

export const passwordHint = 'At least 10 characters, including uppercase, lowercase, a number, and a symbol.';
