import { useEffect, useState } from 'react';
import { Alert, Badge, Button, Card, Col, Container, Nav, Navbar, Row, Spinner } from 'react-bootstrap';
import { Link, Route, Routes } from 'react-router-dom';
import { api } from './api';
import './App.css';

type Account = { id: string; name: string; institution: string; accountType: string; currency: string; lastFourDigits?: string | null; isActive: boolean; createdAtUtc: string };
function Dashboard() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  useEffect(() => { api.get<Account[]>('/api/accounts').then(({ data }) => setAccounts(data)).catch(() => setError('Could not reach the API. Start the backend and check VITE_API_BASE_URL.')).finally(() => setLoading(false)); }, []);
  return <>
    <div className="page-heading"><div><div className="eyebrow">YOUR MONEY, AT A GLANCE</div><h1>Dashboard</h1><p className="text-secondary mb-0">A clear starting point for your financial picture.</p></div><Button variant="dark" disabled>＋ Add account</Button></div>
    {error && <Alert variant="warning">{error}</Alert>}
    <Row className="g-3 mb-4">
      <Col md={4}><Card className="stat-card h-100"><Card.Body><div className="stat-label">FINANCIAL ACCOUNTS</div><div className="stat-value">{loading ? <Spinner size="sm" /> : accounts.length}</div><div className="stat-note">{accounts.length === 1 ? 'account connected' : 'accounts connected'}</div></Card.Body></Card></Col>
      <Col md={4}><Card className="stat-card h-100"><Card.Body><div className="stat-label">TRANSACTIONS</div><div className="stat-value">—</div><div className="stat-note">Available after statement imports</div></Card.Body></Card></Col>
      <Col md={4}><Card className="stat-card h-100"><Card.Body><div className="stat-label">MONTHLY SPENDING</div><div className="stat-value">—</div><div className="stat-note">Summary will appear as data is added</div></Card.Body></Card></Col>
    </Row>
    <Card className="content-card"><Card.Body>
      <div className="d-flex align-items-center justify-content-between mb-3"><div><h2 className="section-title">Your accounts</h2><p className="text-secondary mb-0">Connected financial accounts</p></div><Badge bg="light" text="dark" pill>{loading ? 'Loading' : accounts.length}</Badge></div>
      {loading ? <div className="py-5 text-center"><Spinner animation="border" size="sm" aria-label="Loading accounts" /></div> : accounts.length === 0 ? <div className="empty-state"><div className="empty-icon">＋</div><h3>No accounts yet</h3><p className="text-secondary">Your account list is ready. Account management will arrive in a later phase.</p></div> : <div className="account-list">{accounts.map(account => <div className="account-row" key={account.id}><div><strong>{account.name}</strong><div className="text-secondary small">{account.institution} · {account.accountType}{account.lastFourDigits ? ` · •••• ${account.lastFourDigits}` : ''}</div></div><Badge bg={account.isActive ? 'success' : 'secondary'}>{account.currency}</Badge></div>)}</div>}
    </Card.Body></Card>
  </>;
}
function Accounts() { return <><div className="page-heading"><div><div className="eyebrow">FINANCIAL OVERVIEW</div><h1>Accounts</h1><p className="text-secondary mb-0">Your bank and card accounts in one place.</p></div></div><Alert variant="info">Account creation is not part of Phase 1. The list will populate when accounts are added in a later phase.</Alert></>; }
export default function App() {
  return <div className="app-shell"><Navbar expand="lg" className="topbar"><Container><Navbar.Brand as={Link} to="/" className="brand"><span className="brand-mark">$</span> DollarShark</Navbar.Brand><Navbar.Toggle aria-controls="main-nav"/><Navbar.Collapse id="main-nav"><Nav className="me-auto"><Nav.Link as={Link} to="/">Overview</Nav.Link><Nav.Link as={Link} to="/accounts">Accounts</Nav.Link></Nav><span className="local-label"><span className="status-dot"/> Local workspace</span></Navbar.Collapse></Container></Navbar><main><Container className="main-content"><Routes><Route path="/" element={<Dashboard/>}/><Route path="/accounts" element={<Accounts/>}/></Routes><footer>DollarShark <span>·</span> Personal finance, made clearer.</footer></Container></main></div>;
}
