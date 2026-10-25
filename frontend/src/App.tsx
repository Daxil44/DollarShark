import { Container, Nav, Navbar, Card, Col, Row } from 'react-bootstrap';
import { Link, NavLink, Route, Routes } from 'react-router-dom';
import AccountsPage from './pages/AccountsPage';
import CategoriesPage from './pages/CategoriesPage';
import TransactionsPage from './pages/TransactionsPage';
import './App.css';

function Overview() {
  return <>
    <div className="page-heading"><div><div className="eyebrow">YOUR MONEY, AT A GLANCE</div><h1>DollarShark</h1><p className="text-secondary mb-0">Manage your accounts, organize categories, and record transactions.</p></div></div>
    <Row className="g-3">
      <Col md={4}><Card className="content-card h-100"><Card.Body><h2 className="section-title">Accounts</h2><p className="text-secondary">Create and manage your financial accounts.</p><Link to="/accounts" className="btn btn-outline-dark">Open accounts</Link></Card.Body></Card></Col>
      <Col md={4}><Card className="content-card h-100"><Card.Body><h2 className="section-title">Categories</h2><p className="text-secondary">Organize activity with editable categories.</p><Link to="/categories" className="btn btn-outline-dark">Open categories</Link></Card.Body></Card></Col>
      <Col md={4}><Card className="content-card h-100"><Card.Body><h2 className="section-title">Transactions</h2><p className="text-secondary">Record, filter, and update transactions.</p><Link to="/transactions" className="btn btn-outline-dark">Open transactions</Link></Card.Body></Card></Col>
    </Row>
  </>;
}

export default function App() {
  return <div className="app-shell">
    <Navbar expand="lg" className="topbar"><Container>
      <Navbar.Brand as={Link} to="/" className="brand"><span className="brand-mark">$</span> DollarShark</Navbar.Brand>
      <Navbar.Toggle aria-controls="main-nav" />
      <Navbar.Collapse id="main-nav"><Nav className="me-auto">
        <Nav.Link as={NavLink} to="/" end>Overview</Nav.Link>
        <Nav.Link as={NavLink} to="/accounts">Accounts</Nav.Link>
        <Nav.Link as={NavLink} to="/categories">Categories</Nav.Link>
        <Nav.Link as={NavLink} to="/transactions">Transactions</Nav.Link>
      </Nav><span className="local-label"><span className="status-dot" /> Local workspace</span></Navbar.Collapse>
    </Container></Navbar>
    <main><Container className="main-content">
      <Routes>
        <Route path="/" element={<Overview />} />
        <Route path="/accounts" element={<AccountsPage />} />
        <Route path="/categories" element={<CategoriesPage />} />
        <Route path="/transactions" element={<TransactionsPage />} />
        <Route path="*" element={<Overview />} />
      </Routes>
      <footer>DollarShark <span>·</span> Personal finance, made clearer.</footer>
    </Container></main>
  </div>;
}
