import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Alert, Button, Form, Spinner } from 'react-bootstrap';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api, apiErrorMessage } from '../api';
import AuthLayout, { MailIcon, passwordHint } from '../components/AuthLayout';

/** Reads userId/token from an emailed link once, then removes them from the address bar and history. */
function useLinkParams() {
  const [searchParams] = useSearchParams();
  const [params] = useState(() => ({ userId: searchParams.get('userId') ?? '', token: searchParams.get('token') ?? '' }));
  useEffect(() => {
    if (window.location.search) window.history.replaceState(window.history.state, '', window.location.pathname);
  }, []);
  return params;
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError('');
    try {
      await api.post('/api/auth/forgot-password', { email: email.trim() });
      setSent(true);
    } catch (submitError) {
      setError(apiErrorMessage(submitError));
    } finally {
      setSubmitting(false);
    }
  }

  return <AuthLayout asideTitle="It happens to every shark.">
    {sent ? <div className="text-center">
      <MailIcon />
      <h1 className="h3 fw-bold">Check your email</h1>
      <p className="text-secondary">If an account exists for <strong className="text-body text-break">{email.trim()}</strong>, we've sent a link to reset your password. It expires in 3 hours.</p>
      <Link className="btn btn-dark w-100" to="/login">Back to sign in</Link>
      <Button variant="link" className="small mt-1" onClick={() => setSent(false)}>Use a different email</Button>
    </div> : <>
      <h1 className="h3 fw-bold">Forgot your password?</h1>
      <p className="text-secondary">Enter the email you signed up with and we'll send you a reset link.</p>
      {error && <Alert variant="danger">{error}</Alert>}
      <Form onSubmit={submit}>
        <Form.Group className="mb-3" controlId="forgot-email"><Form.Label>Email</Form.Label><Form.Control type="email" autoComplete="email" required maxLength={256} value={email} onChange={event => setEmail(event.target.value)} autoFocus /></Form.Group>
        <Button type="submit" variant="dark" className="w-100" disabled={submitting}>{submitting ? <><Spinner size="sm" className="me-2" />Sending…</> : 'Send reset link'}</Button>
      </Form>
      <div className="text-center mt-3 small">Remembered it? <Link to="/login">Sign in</Link></div>
    </>}
  </AuthLayout>;
}

export function ResetPasswordPage() {
  const navigate = useNavigate();
  const { userId, token } = useLinkParams();
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const linkMissing = !userId || !token;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (password !== confirm) { setError('The two passwords do not match.'); return; }
    setSubmitting(true);
    setError('');
    try {
      await api.post('/api/auth/reset-password', { userId, token, password });
      navigate('/login', { replace: true, state: { notice: 'Your password has been reset. Sign in with your new password.' } });
    } catch (submitError) {
      setError(apiErrorMessage(submitError));
    } finally {
      setSubmitting(false);
    }
  }

  return <AuthLayout asideTitle="Fresh password, same sharp finances.">
    <h1 className="h3 fw-bold">Choose a new password</h1>
    {linkMissing ? <>
      <Alert variant="danger">This reset link is incomplete. Open the link from your email again, or request a new one.</Alert>
      <Link className="btn btn-dark w-100" to="/forgot-password">Request a new link</Link>
    </> : <>
      <p className="text-secondary">Pick something you haven't used before.</p>
      {error && <Alert variant="danger">{error}{/expired|invalid/i.test(error) && <> <Link to="/forgot-password" className="alert-link">Request a new link</Link>.</>}</Alert>}
      <Form onSubmit={submit}>
        <Form.Group className="mb-3" controlId="reset-password"><Form.Label>New password</Form.Label><Form.Control type="password" autoComplete="new-password" required minLength={10} maxLength={128} value={password} onChange={event => setPassword(event.target.value)} autoFocus /><Form.Text>{passwordHint}</Form.Text></Form.Group>
        <Form.Group className="mb-3" controlId="reset-password-confirm"><Form.Label>Confirm new password</Form.Label><Form.Control type="password" autoComplete="new-password" required maxLength={128} value={confirm} onChange={event => setConfirm(event.target.value)} isInvalid={confirm.length > 0 && confirm !== password} /><Form.Control.Feedback type="invalid">Passwords do not match.</Form.Control.Feedback></Form.Group>
        <Button type="submit" variant="dark" className="w-100" disabled={submitting}>{submitting ? <><Spinner size="sm" className="me-2" />Saving…</> : 'Reset password'}</Button>
      </Form>
    </>}
  </AuthLayout>;
}

export function ConfirmEmailPage() {
  const { userId, token } = useLinkParams();
  const [state, setState] = useState<'working' | 'done' | 'failed'>(() => userId && token ? 'working' : 'failed');
  const [message, setMessage] = useState(userId && token ? '' : 'This confirmation link is incomplete. Open the link from your email again.');
  const started = useRef(false);

  useEffect(() => {
    if (!userId || !token || started.current) return;
    started.current = true;
    api.post<{ message: string }>('/api/auth/confirm-email', { userId, token })
      .then(response => { setMessage(response.data.message); setState('done'); })
      .catch(confirmError => { setMessage(apiErrorMessage(confirmError)); setState('failed'); });
  }, [userId, token]);

  return <AuthLayout asideTitle="Welcome to the pod.">
    <div className="text-center">
      {state === 'working' && <><Spinner animation="border" className="my-3" role="status"><span className="visually-hidden">Confirming</span></Spinner><h1 className="h4 fw-bold">Confirming your email…</h1></>}
      {state === 'done' && <>
        <div className="auth-status-icon success" aria-hidden="true">✓</div>
        <h1 className="h3 fw-bold">You're all set</h1>
        <p className="text-secondary">{message}</p>
        <Link className="btn btn-dark w-100" to="/login">Sign in</Link>
      </>}
      {state === 'failed' && <>
        <div className="auth-status-icon danger" aria-hidden="true">!</div>
        <h1 className="h3 fw-bold">Link didn't work</h1>
        <p className="text-secondary">{message}</p>
        <Link className="btn btn-dark w-100" to="/login">Go to sign in</Link>
        <p className="small text-secondary mt-3 mb-0">Sign in with your email and password and we'll offer to send a fresh link.</p>
      </>}
    </div>
  </AuthLayout>;
}
