import { useState } from 'react';
import { Alert, Button, Container, Nav, Navbar, Spinner } from 'react-bootstrap';
import { Link, Navigate, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { apiErrorMessage } from './api';
import { useAuth } from './auth';
import AccountsPage from './pages/AccountsPage';
import AuthPage from './pages/AuthPage';
import CategoriesPage from './pages/CategoriesPage';
import TransactionsPage from './pages/TransactionsPage';
import StatementsPage from './pages/StatementsPage';
import DashboardPage from './pages/DashboardPage';
import AccountPage from './pages/AccountPage';
import LandingPage from './pages/LandingPage';
import { ConfirmEmailPage, ForgotPasswordPage, ResetPasswordPage } from './pages/AccountEmailPages';
import DollarSharkLogo from './components/DollarSharkLogo';
import ErrorBoundary from './components/ErrorBoundary';
import './App.css';

function ProtectedApp() {
  const { user, checking, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [error, setError] = useState('');
  const [signingOut, setSigningOut] = useState(false);

  if (checking) return <div className="auth-loading"><Spinner animation="border" role="status"><span className="visually-hidden">Checking session…</span></Spinner></div>;
  if (!user) return signingOut
    ? <Navigate to="/" replace />
    : <Navigate to="/login" replace state={{ from: { pathname: location.pathname, search: location.search, hash: location.hash } }} />;

  async function signOut() {
    setError('');
    setSigningOut(true);
    try {
      await logout();
      navigate('/', { replace: true });
    } catch (logoutError) {
      setError(apiErrorMessage(logoutError));
    } finally {
      setSigningOut(false);
    }
  }

  return <div className="app-shell">
    <Navbar expand="lg" className="topbar"><Container>
      <Navbar.Brand as={Link} to="/dashboard" className="brand" aria-label="DollarShark home"><DollarSharkLogo /></Navbar.Brand>
      <Navbar.Toggle aria-controls="main-nav" />
      <Navbar.Collapse id="main-nav"><Nav className="me-auto">
        <Nav.Link as={NavLink} to="/dashboard">Dashboard</Nav.Link>
        <Nav.Link as={NavLink} to="/accounts">Accounts</Nav.Link>
        <Nav.Link as={NavLink} to="/categories">Categories</Nav.Link>
        <Nav.Link as={NavLink} to="/transactions">Transactions</Nav.Link>
        <Nav.Link as={NavLink} to="/statements">Statements</Nav.Link>
      </Nav><div className="user-controls"><Link to="/account" className="local-label account-link" title="Account settings"><span className="status-dot" />{user.email}</Link><Button size="sm" variant="outline-secondary" onClick={() => void signOut()} disabled={signingOut}>{signingOut ? 'Signing out…' : 'Sign out'}</Button></div></Navbar.Collapse>
    </Container></Navbar>
    <main><Container className="main-content">
      {error && <Alert variant="danger" dismissible onClose={() => setError('')}>{error}</Alert>}
      <ErrorBoundary key={location.pathname}><Routes>
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/accounts" element={<AccountsPage />} />
        <Route path="/categories" element={<CategoriesPage />} />
        <Route path="/transactions" element={<TransactionsPage />} />
        <Route path="/statements" element={<StatementsPage />} />
        <Route path="/account" element={<AccountPage />} />
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes></ErrorBoundary>
      <footer>DollarShark <span>·</span> Personal finance, made clearer.</footer>
    </Container></main>
  </div>;
}

function HomeRoute() {
  const { user, checking } = useAuth();
  if (checking) return <div className="auth-loading"><Spinner animation="border" role="status"><span className="visually-hidden">Checking session…</span></Spinner></div>;
  if (user) return <Navigate to="/dashboard" replace />;
  return <LandingPage />;
}

export default function App() {
  return <Routes>
    <Route path="/" element={<HomeRoute />} />
    <Route path="/login" element={<AuthPage mode="login" />} />
    <Route path="/register" element={<AuthPage mode="register" />} />
    <Route path="/forgot-password" element={<ForgotPasswordPage />} />
    <Route path="/reset-password" element={<ResetPasswordPage />} />
    <Route path="/confirm-email" element={<ConfirmEmailPage />} />
    <Route path="*" element={<ProtectedApp />} />
  </Routes>;
}
