import { useState, type FormEvent } from 'react';
import { Alert, Button, Card, Container, Form, Spinner } from 'react-bootstrap';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { apiErrorMessage } from '../api';
import { useAuth } from '../auth';
import DollarSharkLogo from '../components/DollarSharkLogo';

export default function AuthPage({ mode }: { mode: 'login' | 'register' }) {
  const { user, login, register } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const isRegister = mode === 'register';
  const from = (location.state as { from?: string | { pathname?: string; search?: string; hash?: string } } | null)?.from;
  const destination = typeof from === 'string' ? from : from?.pathname ? `${from.pathname}${from.search ?? ''}${from.hash ?? ''}` : '/dashboard';

  if (user) return <Navigate to={destination === '/login' || destination === '/register' ? '/dashboard' : destination} replace />;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    setSubmitting(true);
    try {
      if (isRegister) await register(email.trim(), password);
      else await login(email.trim(), password);
      navigate(destination, { replace: true });
    } catch (submitError) {
      setError(apiErrorMessage(submitError));
    } finally {
      setSubmitting(false);
    }
  }

  return <main className="auth-split">
    <aside className="auth-aside" aria-hidden="true">
      <Link to="/" className="auth-aside-logo" tabIndex={-1}><DollarSharkLogo /></Link>
      <div className="auth-aside-copy">
        <h2>{isRegister ? 'Start swimming toward clearer finances.' : 'Good to see you again.'}</h2>
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
      <h1 className="h3 fw-bold">{isRegister ? 'Create your account' : 'Welcome back'}</h1>
      <p className="text-secondary">{isRegister ? 'Your finance data will be private to your account.' : 'Sign in to see your private finance workspace.'}</p>
      {error && <Alert variant="danger">{error}</Alert>}
      <Form onSubmit={submit}>
        <Form.Group className="mb-3"><Form.Label>Email</Form.Label><Form.Control type="email" autoComplete="email" required maxLength={256} value={email} onChange={event => setEmail(event.target.value)} /></Form.Group>
        <Form.Group className="mb-3"><Form.Label>Password</Form.Label><Form.Control type="password" autoComplete={isRegister ? 'new-password' : 'current-password'} required minLength={isRegister ? 10 : undefined} maxLength={128} value={password} onChange={event => setPassword(event.target.value)} />
          {isRegister && <Form.Text>At least 10 characters, including uppercase, lowercase, a number, and a symbol.</Form.Text>}
        </Form.Group>
        <Button type="submit" variant="dark" className="w-100" disabled={submitting}>{submitting ? <><Spinner size="sm" className="me-2" />Please wait…</> : isRegister ? 'Create account' : 'Sign in'}</Button>
      </Form>
      <div className="text-center mt-3 small">{isRegister ? <>Already registered? <Link to="/login">Sign in</Link></> : <>New to DollarShark? <Link to="/register">Create an account</Link></>}</div>
      {isRegister && <div className="small text-secondary mt-3">Email confirmation and password recovery are not available yet. Use a unique password.</div>}
    </Card.Body></Card>
  </Container></section></main>;
}
