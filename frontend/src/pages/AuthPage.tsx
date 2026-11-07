import { useState, type FormEvent } from 'react';
import { Alert, Button, Form, Spinner } from 'react-bootstrap';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { api, apiErrorMessage } from '../api';
import { isEmailNotConfirmed, useAuth } from '../auth';
import AuthLayout, { MailIcon, passwordHint } from '../components/AuthLayout';

export default function AuthPage({ mode }: { mode: 'login' | 'register' }) {
  const { user, login, register } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [unconfirmed, setUnconfirmed] = useState(false);
  const [checkInboxFor, setCheckInboxFor] = useState('');
  const [resendState, setResendState] = useState<'idle' | 'sending' | 'sent'>('idle');
  const isRegister = mode === 'register';
  const from = (location.state as { from?: string | { pathname?: string; search?: string; hash?: string } } | null)?.from;
  const destination = typeof from === 'string' ? from : from?.pathname ? `${from.pathname}${from.search ?? ''}${from.hash ?? ''}` : '/dashboard';
  const notice = (location.state as { notice?: string } | null)?.notice;

  if (user) return <Navigate to={destination === '/login' || destination === '/register' ? '/dashboard' : destination} replace />;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError('');
    setUnconfirmed(false);
    setResendState('idle');
    setSubmitting(true);
    try {
      if (isRegister) {
        const outcome = await register(email.trim(), password);
        if (outcome === 'confirmationRequired') { setCheckInboxFor(email.trim()); return; }
      } else {
        await login(email.trim(), password);
      }
      navigate(destination, { replace: true });
    } catch (submitError) {
      if (isEmailNotConfirmed(submitError)) setUnconfirmed(true);
      else setError(apiErrorMessage(submitError));
    } finally {
      setSubmitting(false);
    }
  }

  async function resend(address: string) {
    setResendState('sending');
    setError('');
    try {
      await api.post('/api/auth/resend-confirmation', { email: address });
      setResendState('sent');
    } catch (resendError) {
      setResendState('idle');
      setError(apiErrorMessage(resendError));
    }
  }

  if (checkInboxFor) return <AuthLayout asideTitle="Almost there — check your inbox.">
    <div className="text-center">
      <MailIcon />
      <h1 className="h3 fw-bold">Check your email</h1>
      <p className="text-secondary">We sent a confirmation link to <strong className="text-body text-break">{checkInboxFor}</strong>. Open it to activate your account, then sign in.</p>
      {error && <Alert variant="danger" className="text-start">{error}</Alert>}
      {resendState === 'sent' && <Alert variant="success" className="text-start">A new confirmation link is on its way.</Alert>}
      <Link className="btn btn-dark w-100 mb-2" to="/login">Go to sign in</Link>
      <Button variant="link" className="small" disabled={resendState === 'sending'} onClick={() => void resend(checkInboxFor)}>{resendState === 'sending' ? 'Sending…' : "Didn't get it? Resend the link"}</Button>
      <p className="small text-secondary mt-2 mb-0">The link expires in 3 hours. Check your spam folder too.</p>
    </div>
  </AuthLayout>;

  return <AuthLayout asideTitle={isRegister ? 'Start swimming toward clearer finances.' : 'Good to see you again.'}>
    <h1 className="h3 fw-bold">{isRegister ? 'Create your account' : 'Welcome back'}</h1>
    <p className="text-secondary">{isRegister ? 'Your finance data will be private to your account.' : 'Sign in to see your private finance workspace.'}</p>
    {notice && !error && !unconfirmed && <Alert variant="success">{notice}</Alert>}
    {error && <Alert variant="danger">{error}</Alert>}
    {unconfirmed && <Alert variant="warning">
      <div className="fw-semibold mb-1">Confirm your email first</div>
      <div className="small mb-2">Open the confirmation link we emailed to <strong>{email.trim()}</strong>, then sign in.</div>
      {resendState === 'sent' ? <div className="small fw-semibold">New link sent — check your inbox.</div> :
        <Button size="sm" variant="outline-dark" disabled={resendState === 'sending'} onClick={() => void resend(email.trim())}>{resendState === 'sending' ? 'Sending…' : 'Resend confirmation email'}</Button>}
    </Alert>}
    <Form onSubmit={submit}>
      <Form.Group className="mb-3" controlId="auth-email"><Form.Label>Email</Form.Label><Form.Control type="email" autoComplete="email" required maxLength={256} value={email} onChange={event => setEmail(event.target.value)} /></Form.Group>
      <Form.Group className="mb-3" controlId="auth-password">
        <div className="d-flex justify-content-between align-items-baseline"><Form.Label>Password</Form.Label>{!isRegister && <Link to="/forgot-password" className="small">Forgot password?</Link>}</div>
        <Form.Control type="password" autoComplete={isRegister ? 'new-password' : 'current-password'} required minLength={isRegister ? 10 : undefined} maxLength={128} value={password} onChange={event => setPassword(event.target.value)} />
        {isRegister && <Form.Text>{passwordHint}</Form.Text>}
      </Form.Group>
      <Button type="submit" variant="dark" className="w-100" disabled={submitting}>{submitting ? <><Spinner size="sm" className="me-2" />Please wait…</> : isRegister ? 'Create account' : 'Sign in'}</Button>
    </Form>
    <div className="text-center mt-3 small">{isRegister ? <>Already registered? <Link to="/login">Sign in</Link></> : <>New to DollarShark? <Link to="/register">Create an account</Link></>}</div>
    {isRegister && <div className="small text-secondary mt-3">We'll email you a link to confirm your address before your first sign-in.</div>}
  </AuthLayout>;
}
