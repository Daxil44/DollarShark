import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Form, Modal, Spinner, Table } from 'react-bootstrap';
import { api, apiErrorMessage } from '../api';
import type { Account, AccountInput } from '../types';

const blankForm = (): AccountInput => ({ name: '', institution: '', accountType: 'Chequing', currency: 'CAD', lastFourDigits: '' });

export default function AccountsPage() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [includeInactive, setIncludeInactive] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Account | null>(null);
  const [form, setForm] = useState<AccountInput>(blankForm());

  const loadAccounts = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await api.get<Account[]>('/api/accounts', { params: { includeInactive } });
      setAccounts(response.data);
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [includeInactive]);

  useEffect(() => { void loadAccounts(); }, [loadAccounts]);

  function openCreate() {
    setEditing(null);
    setForm(blankForm());
    setError('');
    setShowForm(true);
  }

  function openEdit(account: Account) {
    setEditing(account);
    setForm({
      name: account.name,
      institution: account.institution,
      accountType: account.accountType,
      currency: account.currency,
      lastFourDigits: account.lastFourDigits ?? '',
    });
    setError('');
    setShowForm(true);
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError('');
    try {
      if (editing) await api.put('/api/accounts/' + editing.id, { ...form, lastFourDigits: form.lastFourDigits || null });
      else await api.post('/api/accounts', { ...form, lastFourDigits: form.lastFourDigits || null });
      setShowForm(false);
      setNotice(editing ? 'Account updated.' : 'Account created.');
      await loadAccounts();
    } catch (saveError) {
      setError(apiErrorMessage(saveError));
    } finally {
      setSaving(false);
    }
  }

  async function toggleStatus(account: Account) {
    setError('');
    setNotice('');
    try {
      await api.patch('/api/accounts/' + account.id + '/status', { isActive: !account.isActive });
      setNotice(account.isActive ? 'Account deactivated. Its transaction history is preserved.' : 'Account reactivated.');
      await loadAccounts();
    } catch (statusError) {
      setError(apiErrorMessage(statusError));
    }
  }

  return <>
    <div className="page-heading">
      <div><div className="eyebrow">FINANCIAL OVERVIEW</div><h1>Accounts</h1><p className="text-secondary mb-0">Manage chequing, savings, and credit card accounts.</p></div>
      <Button variant="dark" onClick={openCreate}>＋ Add account</Button>
    </div>
    {error && <Alert variant="danger" dismissible onClose={() => setError('')}>{error}</Alert>}
    {notice && <Alert variant="success" dismissible onClose={() => setNotice('')}>{notice}</Alert>}
    <div className="d-flex justify-content-end mb-3">
      <Form.Check type="switch" id="include-inactive" label="Show inactive accounts" checked={includeInactive} onChange={event => setIncludeInactive(event.target.checked)} />
    </div>
    <div className="content-card p-3 p-md-4">
      {loading ? <div className="text-center py-5"><Spinner animation="border" size="sm" aria-label="Loading accounts" /></div> :
        accounts.length === 0 ? <div className="empty-state"><div className="empty-icon">＋</div><h3>No accounts yet</h3><p className="text-secondary">Add an account to start recording transactions.</p></div> :
        <Table responsive hover className="align-middle mb-0">
          <thead><tr><th>Account</th><th>Type</th><th>Currency</th><th>Status</th><th className="text-end">Actions</th></tr></thead>
          <tbody>{accounts.map(account => <tr key={account.id}>
            <td><strong>{account.name}</strong><div className="small text-secondary">{account.institution}{account.lastFourDigits ? ' · •••• ' + account.lastFourDigits : ''}</div></td>
            <td>{account.accountType}</td><td>{account.currency}</td>
            <td><Badge bg={account.isActive ? 'success' : 'secondary'}>{account.isActive ? 'Active' : 'Inactive'}</Badge></td>
            <td className="text-end text-nowrap"><Button size="sm" variant="outline-secondary" className="me-2" onClick={() => openEdit(account)}>Edit</Button><Button size="sm" variant={account.isActive ? 'outline-danger' : 'outline-success'} onClick={() => void toggleStatus(account)}>{account.isActive ? 'Deactivate' : 'Reactivate'}</Button></td>
          </tr>)}</tbody>
        </Table>}
    </div>
    <Modal show={showForm} onHide={() => setShowForm(false)} centered>
      <Form onSubmit={save}>
        <Modal.Header closeButton><Modal.Title>{editing ? 'Edit account' : 'Add account'}</Modal.Title></Modal.Header>
        <Modal.Body>
          {error && <Alert variant="danger">{error}</Alert>}
          <Form.Group className="mb-3"><Form.Label>Account name</Form.Label><Form.Control required maxLength={120} value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} placeholder="Everyday chequing" /></Form.Group>
          <Form.Group className="mb-3"><Form.Label>Institution</Form.Label><Form.Control required maxLength={120} value={form.institution} onChange={event => setForm({ ...form, institution: event.target.value })} placeholder="Bank or credit union" /></Form.Group>
          <Form.Group className="mb-3"><Form.Label>Account type</Form.Label><Form.Select value={form.accountType} onChange={event => setForm({ ...form, accountType: event.target.value })}><option>Chequing</option><option>Savings</option><option>Credit Card</option></Form.Select></Form.Group>
          <div className="row"><Form.Group className="col-sm-6 mb-3"><Form.Label>Currency</Form.Label><Form.Control required maxLength={3} minLength={3} value={form.currency} onChange={event => setForm({ ...form, currency: event.target.value.toUpperCase() })} /></Form.Group><Form.Group className="col-sm-6 mb-3"><Form.Label>Last four digits (optional)</Form.Label><Form.Control inputMode="numeric" maxLength={4} value={form.lastFourDigits} onChange={event => setForm({ ...form, lastFourDigits: event.target.value })} /></Form.Group></div>
          <div className="small text-secondary">Never enter a full card number, CVV, or banking password.</div>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setShowForm(false)}>Cancel</Button><Button variant="dark" type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save account'}</Button></Modal.Footer>
      </Form>
    </Modal>
  </>;
}
