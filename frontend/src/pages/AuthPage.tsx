import { useState, type FormEvent } from 'react';
import { Alert, Button, Card, Container, Form, Spinner } from 'react-bootstrap';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { apiErrorMessage } from '../api';
import { useAuth } from '../auth';

export default function AuthPage({ mode }: { mode: 'login' | 'register' }) {
  const { user, login, register } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const isRegister = mode === 'register';
  const destination = (location.state as { from?: string } | null)?.from ?? '/';

  if (user) return <Navigate to="/" replace />;

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

  return <main className="auth-background"><Container className="auth-container">
    <Card className="auth-card"><Card.Body>
      <div className="brand mb-4"><span className="brand-mark">$</span> DollarShark</div>
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
  </Container></main>;
}
