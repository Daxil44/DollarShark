import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Form, Modal, Pagination, Spinner, Table } from 'react-bootstrap';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api, apiErrorMessage } from '../api';
import type { Account, Category, Transaction, TransactionInput, TransactionPage, TransferSuggestion, TransferTransactionSummary } from '../types';

type Filters = { search: string; accountId: string; categoryId: string; transactionType: string; year: string; month: string; from: string; to: string };
type TransactionForm = { transactionDate: string; description: string; amount: string; transactionType: string; financialAccountId: string; categoryId: string };
const emptyFilters = (): Filters => ({ search: '', accountId: '', categoryId: '', transactionType: '', year: '', month: '', from: '', to: '' });
const today = () => new Date().toISOString().slice(0, 10);
const emptyForm = (): TransactionForm => ({ transactionDate: today(), description: '', amount: '', transactionType: 'Expense', financialAccountId: '', categoryId: '' });
const pageSize = 25;
const suggestionKey = (suggestion: TransferSuggestion) => [suggestion.outgoing.id, suggestion.incoming.id].sort().join(':');

export default function TransactionsPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [page, setPage] = useState<TransactionPage>({ items: [], page: 1, pageSize, totalCount: 0, totalPages: 0 });
  const [filters, setFilters] = useState<Filters>(emptyFilters());
  const [appliedFilters, setAppliedFilters] = useState<Filters>(emptyFilters());
  const [sortBy, setSortBy] = useState('date');
  const [sortDirection, setSortDirection] = useState('desc');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Transaction | null>(null);
  const [form, setForm] = useState<TransactionForm>(emptyForm());
  const [transferSuggestions, setTransferSuggestions] = useState<TransferSuggestion[]>([]);
  const [dismissedSuggestions, setDismissedSuggestions] = useState<Set<string>>(new Set());
  const [selectedTransferIds, setSelectedTransferIds] = useState<string[]>([]);
  const [pendingTransfer, setPendingTransfer] = useState<{ outgoing: TransferTransactionSummary; incoming: TransferTransactionSummary } | null>(null);
  const [viewingCounterpart, setViewingCounterpart] = useState<Transaction | null>(null);
  const [savingTransfer, setSavingTransfer] = useState(false);

  useEffect(() => {
    void Promise.all([
      api.get<Account[]>('/api/accounts'),
      api.get<Category[]>('/api/categories'),
    ]).then(([accountResponse, categoryResponse]) => {
      setAccounts(accountResponse.data);
      setCategories(categoryResponse.data);
    }).catch(loadError => setError(apiErrorMessage(loadError)));
  }, []);

  const loadTransactions = useCallback(async () => {
    setLoading(true);
    setError('');
    const params = new URLSearchParams({ page: String(page.page), pageSize: String(pageSize), sortBy, sortDirection });
    Object.entries(appliedFilters).forEach(([key, value]) => { if (value) params.set(key, value); });
    try {
      const response = await api.get<TransactionPage>('/api/transactions?' + params.toString());
      setPage(response.data);
      setTransactions(response.data.items);
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [appliedFilters, page.page, sortBy, sortDirection]);

  useEffect(() => { void loadTransactions(); }, [loadTransactions]);
  useEffect(() => { setSelectedTransferIds([]); }, [appliedFilters, page.page]);

  const loadTransferSuggestions = useCallback(async () => {
    try {
      const response = await api.get<TransferSuggestion[]>('/api/transfers/suggestions');
      setTransferSuggestions(response.data);
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    }
  }, []);

  useEffect(() => { void loadTransferSuggestions(); }, [loadTransferSuggestions]);

  const focusedTransactionId = searchParams.get('focus');
  useEffect(() => {
    if (!focusedTransactionId) {
      setViewingCounterpart(null);
      return;
    }
    let cancelled = false;
    api.get<Transaction>('/api/transactions/' + focusedTransactionId)
      .then(response => { if (!cancelled) setViewingCounterpart(response.data); })
      .catch(loadError => { if (!cancelled) setError(apiErrorMessage(loadError)); });
    return () => { cancelled = true; };
  }, [focusedTransactionId]);

  function openCreate() {
    setEditing(null);
    const defaultAccount = accounts[0];
    setForm({
      ...emptyForm(),
      financialAccountId: defaultAccount?.id ?? '',
      transactionType: defaultAccount?.accountType === 'Investment' ? 'Transfer' : 'Expense',
    });
    setError('');
    setShowForm(true);
  }

  function openEdit(transaction: Transaction) {
    setEditing(transaction);
    setForm({
      transactionDate: transaction.transactionDate,
      description: transaction.description,
      amount: String(transaction.amount),
      transactionType: transaction.transactionType,
      financialAccountId: transaction.financialAccountId,
      categoryId: transaction.categoryId ?? '',
    });
    setError('');
    setShowForm(true);
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError('');
    const body: TransactionInput = {
      transactionDate: form.transactionDate,
      description: form.description,
      amount: Number(form.amount),
      transactionType: form.transactionType,
      financialAccountId: form.financialAccountId,
      categoryId: form.categoryId || null,
    };
    try {
      if (editing) await api.put('/api/transactions/' + editing.id, body);
      else await api.post('/api/transactions', body);
      setShowForm(false);
      setNotice(editing ? 'Transaction updated.' : 'Transaction created.');
      await loadTransactions();
    } catch (saveError) {
      setError(apiErrorMessage(saveError));
    } finally {
      setSaving(false);
    }
  }

  async function remove(transaction: Transaction) {
    if (!window.confirm('Delete transaction "' + transaction.description + '"?')) return;
    setError('');
    setNotice('');
    try {
      await api.delete('/api/transactions/' + transaction.id);
      setNotice('Transaction deleted.');
      await loadTransactions();
    } catch (deleteError) {
      setError(apiErrorMessage(deleteError));
    }
  }

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPage(current => ({ ...current, page: 1 }));
    setAppliedFilters({ ...filters });
  }

  function changeSort(field: string) {
    if (sortBy === field) setSortDirection(current => current === 'asc' ? 'desc' : 'asc');
    else {
      setSortBy(field);
      setSortDirection(field === 'date' ? 'desc' : 'asc');
    }
    setPage(current => ({ ...current, page: 1 }));
  }

  function formatAmount(transaction: Transaction) {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency: transaction.currency }).format(transaction.amount);
  }

  function summary(transaction: Transaction): TransferTransactionSummary {
    return {
      id: transaction.id, transactionDate: transaction.transactionDate, description: transaction.description,
      amount: transaction.amount, currency: transaction.currency, financialAccountId: transaction.financialAccountId,
      accountName: transaction.accountName,
    };
  }

  function reviewSuggestion(suggestion: TransferSuggestion) {
    setPendingTransfer({ outgoing: suggestion.outgoing, incoming: suggestion.incoming });
  }

  function reviewSelectedTransfers() {
    const selected = selectedTransferIds.map(id => transactions.find(transaction => transaction.id === id)).filter((transaction): transaction is Transaction => Boolean(transaction));
    const outgoing = selected.find(transaction => transaction.amount < 0);
    const incoming = selected.find(transaction => transaction.amount > 0);
    if (!outgoing || !incoming) {
      setError('Select one negative outgoing transfer and one positive incoming transfer.');
      return;
    }
    setPendingTransfer({ outgoing: summary(outgoing), incoming: summary(incoming) });
  }

  async function confirmTransferLink() {
    if (!pendingTransfer) return;
    setSavingTransfer(true);
    setError('');
    try {
      await api.post('/api/transfers/link', {
        outgoingTransactionId: pendingTransfer.outgoing.id,
        incomingTransactionId: pendingTransfer.incoming.id,
      });
      setPendingTransfer(null);
      setSelectedTransferIds([]);
      setNotice('Transfer linked. Both transactions remain in account history.');
      await Promise.all([loadTransactions(), loadTransferSuggestions()]);
    } catch (linkError) {
      setError(apiErrorMessage(linkError));
    } finally {
      setSavingTransfer(false);
    }
  }

  async function unlinkTransfer(transaction: Transaction) {
    if (!window.confirm(`Unlink the transfer between “${transaction.description}” and “${transaction.linkedTransferCounterpart?.description ?? 'its counterpart'}”?`)) return;
    setError('');
    try {
      await api.delete('/api/transfers/' + transaction.id);
      setNotice('Transfer unlinked. Both transactions were preserved.');
      await Promise.all([loadTransactions(), loadTransferSuggestions()]);
    } catch (unlinkError) {
      setError(apiErrorMessage(unlinkError));
    }
  }

  function toggleTransferSelection(transaction: Transaction) {
    if (selectedTransferIds.includes(transaction.id)) {
      setSelectedTransferIds(current => current.filter(id => id !== transaction.id));
      return;
    }
    if (selectedTransferIds.length >= 2) {
      setError('Select only two transactions to link as a transfer.');
      return;
    }
    setError('');
    setSelectedTransferIds(current => [...current, transaction.id]);
  }

  const totalPages = Math.max(page.totalPages, 1);
  return <>
    <div className="page-heading">
      <div><div className="eyebrow">YOUR ACTIVITY</div><h1>Transactions</h1><p className="text-secondary mb-0">Record and find activity across your accounts.</p></div>
      <div className="d-flex flex-wrap gap-2">
        <Button variant="outline-primary" onClick={reviewSelectedTransfers} disabled={selectedTransferIds.length !== 2}>Review {selectedTransferIds.length} selected transfers</Button>
        <Button variant="dark" onClick={openCreate} disabled={accounts.length === 0}>＋ Add transaction</Button>
      </div>
    </div>
    {error && <Alert variant="danger" dismissible onClose={() => setError('')}>{error}</Alert>}
    {notice && <Alert variant="success" dismissible onClose={() => setNotice('')}>{notice}</Alert>}
    {accounts.length === 0 && !loading && <Alert variant="info">Add an active account before creating transactions.</Alert>}
    {transferSuggestions.filter(suggestion => !dismissedSuggestions.has(suggestionKey(suggestion))).length > 0 && <div className="content-card p-3 p-md-4 mb-3">
      <div className="d-flex justify-content-between align-items-start gap-3 mb-3"><div><div className="eyebrow">RECONCILIATION</div><h2 className="h5 mb-1">Possible transfer matches</h2><p className="text-secondary mb-0">Suggestions never link transactions automatically. Review each pair before linking.</p></div><Badge bg="light" text="dark">{transferSuggestions.filter(suggestion => !dismissedSuggestions.has(suggestionKey(suggestion))).length} suggestions</Badge></div>
      <div className="d-grid gap-2">{transferSuggestions.filter(suggestion => !dismissedSuggestions.has(suggestionKey(suggestion))).map(suggestion => <div key={suggestionKey(suggestion)} className="border rounded p-3 d-flex flex-column flex-lg-row justify-content-between align-items-lg-center gap-3">
        <div className="flex-grow-1"><strong>{suggestion.outgoing.description}</strong> · {suggestion.outgoing.accountName} <span className="text-secondary">→</span> <strong>{suggestion.incoming.description}</strong> · {suggestion.incoming.accountName}<div className="small text-secondary">{suggestion.matchReason} {new Intl.NumberFormat(undefined, { style: 'currency', currency: suggestion.outgoing.currency }).format(Math.abs(suggestion.outgoing.amount))}</div></div>
        <div className="d-flex gap-2"><Button size="sm" variant="outline-primary" onClick={() => reviewSuggestion(suggestion)}>Review match</Button><Button size="sm" variant="outline-secondary" onClick={() => setDismissedSuggestions(current => new Set(current).add(suggestionKey(suggestion)))}>Dismiss</Button></div>
      </div>)}</div>
    </div>}
    <div className="content-card p-3 p-md-4 mb-3">
      <Form onSubmit={applyFilters}>
        <div className="row g-2 align-items-end">
          <Form.Group className="col-12 col-md-4"><Form.Label>Search description</Form.Label><Form.Control value={filters.search} maxLength={100} onChange={event => setFilters({ ...filters, search: event.target.value })} placeholder="Search transactions" /></Form.Group>
          <Form.Group className="col-6 col-md-2"><Form.Label>Account</Form.Label><Form.Select value={filters.accountId} onChange={event => setFilters({ ...filters, accountId: event.target.value })}><option value="">All accounts</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-6 col-md-2"><Form.Label>Category</Form.Label><Form.Select value={filters.categoryId} onChange={event => setFilters({ ...filters, categoryId: event.target.value })}><option value="">All categories</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-6 col-md-2"><Form.Label>Type</Form.Label><Form.Select value={filters.transactionType} onChange={event => setFilters({ ...filters, transactionType: event.target.value })}><option value="">All types</option><option>Expense</option><option>Income</option><option>Refund</option><option>Transfer</option></Form.Select></Form.Group>
          <Form.Group className="col-6 col-md-1"><Form.Label>Year</Form.Label><Form.Control type="number" min="1900" max="9999" value={filters.year} onChange={event => setFilters({ ...filters, year: event.target.value })} /></Form.Group>
          <Form.Group className="col-6 col-md-1"><Form.Label>Month</Form.Label><Form.Select value={filters.month} onChange={event => setFilters({ ...filters, month: event.target.value })}><option value="">Any</option>{Array.from({ length: 12 }, (_, index) => <option key={index + 1} value={index + 1}>{index + 1}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-6 col-md-3"><Form.Label>From</Form.Label><Form.Control type="date" value={filters.from} onChange={event => setFilters({ ...filters, from: event.target.value })} /></Form.Group>
          <Form.Group className="col-6 col-md-3"><Form.Label>To</Form.Label><Form.Control type="date" value={filters.to} onChange={event => setFilters({ ...filters, to: event.target.value })} /></Form.Group>
          <div className="col-12 col-md-2"><Button type="submit" variant="outline-dark" className="w-100">Apply filters</Button></div>
        </div>
      </Form>
    </div>
    <div className="content-card p-2 p-md-3">
      {loading ? <div className="text-center py-5"><Spinner animation="border" size="sm" aria-label="Loading transactions" /></div> :
        transactions.length === 0 ? <div className="empty-state"><div className="empty-icon">↔</div><h3>No transactions found</h3><p className="text-secondary">Add a transaction or adjust the filters.</p></div> :
        <Table responsive hover className="align-middle mb-0">
          <thead><tr>
            <th>Select</th><th><Button variant="link" className="p-0 text-decoration-none text-dark fw-semibold" onClick={() => changeSort('date')}>Date {sortBy === 'date' ? (sortDirection === 'asc' ? '↑' : '↓') : ''}</Button></th>
            <th>Description</th><th>Account</th><th>Category</th>
            <th><Button variant="link" className="p-0 text-decoration-none text-dark fw-semibold" onClick={() => changeSort('amount')}>Amount {sortBy === 'amount' ? (sortDirection === 'asc' ? '↑' : '↓') : ''}</Button></th>
            <th>Type</th><th>Transfer link</th><th className="text-end">Actions</th>
          </tr></thead>
          <tbody>{transactions.map(transaction => <tr key={transaction.id}>
            <td>{transaction.transactionType === 'Transfer' && !transaction.linkedTransferTransactionId && transaction.amount !== 0 && <Form.Check aria-label={`Select ${transaction.description} to link as a transfer`} checked={selectedTransferIds.includes(transaction.id)} disabled={!selectedTransferIds.includes(transaction.id) && selectedTransferIds.length >= 2} onChange={() => toggleTransferSelection(transaction)} />}</td>
            <td className="text-nowrap">{transaction.transactionDate}</td>
            <td><strong>{transaction.description}</strong>{transaction.subDescription && <div className="small text-secondary">{transaction.subDescription}</div>}</td>
            <td>{transaction.accountName}</td><td>{transaction.categoryName ?? 'Uncategorized'}</td>
            <td className={transaction.amount < 0 ? 'text-danger text-nowrap' : 'text-success text-nowrap'}>{formatAmount(transaction)}</td>
            <td><Badge bg={transaction.transactionType === 'Expense' ? 'secondary' : transaction.transactionType === 'Transfer' ? 'info' : 'success'} text={transaction.transactionType === 'Transfer' ? 'dark' : undefined}>{transaction.transactionType}</Badge></td>
            <td>{transaction.linkedTransferCounterpart ? <Badge bg="success">Linked</Badge> : transaction.transactionType === 'Transfer' ? <Badge bg="light" text="dark">Unlinked</Badge> : <span className="text-secondary">—</span>}</td>
            <td className="text-end text-nowrap">{transaction.linkedTransferCounterpart ? <><Button size="sm" variant="outline-primary" className="me-2" onClick={() => navigate(`/transactions?focus=${transaction.linkedTransferCounterpart!.id}`)}>View counterpart</Button><Button size="sm" variant="outline-danger" onClick={() => void unlinkTransfer(transaction)}>Unlink</Button></> : <><Button size="sm" variant="outline-secondary" className="me-2" onClick={() => openEdit(transaction)}>Edit</Button><Button size="sm" variant="outline-danger" onClick={() => void remove(transaction)}>Delete</Button></>}</td>
          </tr>)}</tbody>
        </Table>}
      <div className="d-flex justify-content-between align-items-center flex-wrap gap-2 px-2 pt-3">
        <span className="small text-secondary">{page.totalCount} transaction{page.totalCount === 1 ? '' : 's'} · Page {Math.min(page.page, totalPages)} of {totalPages}</span>
        <Pagination className="mb-0"><Pagination.Prev disabled={page.page <= 1 || loading} onClick={() => setPage(current => ({ ...current, page: current.page - 1 }))} /><Pagination.Next disabled={page.page >= totalPages || loading} onClick={() => setPage(current => ({ ...current, page: current.page + 1 }))} /></Pagination>
      </div>
    </div>
    <Modal show={showForm} onHide={() => setShowForm(false)} centered>
      <Form onSubmit={save}>
        <Modal.Header closeButton><Modal.Title>{editing ? 'Edit transaction' : 'Add transaction'}</Modal.Title></Modal.Header>
        <Modal.Body>
          {error && <Alert variant="danger">{error}</Alert>}
          <div className="row"><Form.Group className="col-sm-6 mb-3"><Form.Label>Date</Form.Label><Form.Control type="date" required value={form.transactionDate} onChange={event => setForm({ ...form, transactionDate: event.target.value })} /></Form.Group>
            <Form.Group className="col-sm-6 mb-3"><Form.Label>Type</Form.Label><Form.Select value={form.transactionType} onChange={event => setForm({ ...form, transactionType: event.target.value })}><option>Expense</option><option>Income</option><option>Refund</option><option>Transfer</option></Form.Select></Form.Group></div>
          <Form.Group className="mb-3"><Form.Label>Description</Form.Label><Form.Control required maxLength={300} value={form.description} onChange={event => setForm({ ...form, description: event.target.value })} /></Form.Group>
          <Form.Group className="mb-3"><Form.Label>Amount</Form.Label><Form.Control required type="number" step="0.0001" value={form.amount} onChange={event => setForm({ ...form, amount: event.target.value })} placeholder={form.transactionType === 'Expense' ? '-24.99' : '24.99'} /><Form.Text>Expenses are negative; income and refunds are positive. Transfers may use either sign.</Form.Text></Form.Group>
          <Form.Group className="mb-3"><Form.Label>Account</Form.Label><Form.Select required value={form.financialAccountId} onChange={event => {
            const account = accounts.find(candidate => candidate.id === event.target.value);
            setForm({ ...form, financialAccountId: event.target.value, ...(account?.accountType === 'Investment' ? { transactionType: 'Transfer' } : {}) });
          }}><option value="">Choose an account</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.name} · {account.currency}</option>)}</Form.Select>
            {accounts.find(account => account.id === form.financialAccountId)?.accountType === 'Investment' && <Form.Text>Investment contributions and withdrawals should be classified as transfers so they stay out of income and expense totals.</Form.Text>}
          </Form.Group>
          <Form.Group><Form.Label>Category (optional)</Form.Label><Form.Select value={form.categoryId} onChange={event => setForm({ ...form, categoryId: event.target.value })}><option value="">Uncategorized</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}</Form.Select></Form.Group>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setShowForm(false)}>Cancel</Button><Button variant="dark" type="submit" disabled={saving || accounts.length === 0}>{saving ? 'Saving…' : 'Save transaction'}</Button></Modal.Footer>
      </Form>
    </Modal>
    <Modal show={pendingTransfer !== null} onHide={() => setPendingTransfer(null)} centered>
      <Modal.Header closeButton><Modal.Title>Confirm transfer pair</Modal.Title></Modal.Header>
      <Modal.Body>
        <p className="text-secondary">Review both existing transactions. Linking does not change their amounts or remove them from either account’s history.</p>
        {pendingTransfer && <div className="d-grid gap-3">
          {[pendingTransfer.outgoing, pendingTransfer.incoming].map((item, index) => <div key={item.id} className="border rounded p-3">
            <Badge bg={index === 0 ? 'secondary' : 'primary'} className="mb-2">{index === 0 ? 'Outgoing' : 'Incoming'}</Badge>
            <div><strong>{item.description}</strong></div><div>{item.accountName} · {item.transactionDate}</div>
            <div className={item.amount < 0 ? 'text-danger fw-semibold' : 'text-success fw-semibold'}>{new Intl.NumberFormat(undefined, { style: 'currency', currency: item.currency }).format(item.amount)}</div>
          </div>)}
        </div>}
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => setPendingTransfer(null)}>Cancel</Button><Button variant="dark" onClick={() => void confirmTransferLink()} disabled={savingTransfer}>{savingTransfer ? 'Linking…' : 'Confirm link'}</Button></Modal.Footer>
    </Modal>
    <Modal show={viewingCounterpart !== null} onHide={() => navigate('/transactions')} centered>
      <Modal.Header closeButton><Modal.Title>Transfer counterpart</Modal.Title></Modal.Header>
      <Modal.Body>{viewingCounterpart ? <>
        <Badge bg="info" text="dark" className="mb-2">{viewingCounterpart.transactionType}</Badge>
        <h3 className="h5">{viewingCounterpart.description}</h3>
        <div>{viewingCounterpart.accountName} · {viewingCounterpart.transactionDate}</div>
        <div className={viewingCounterpart.amount < 0 ? 'text-danger fw-semibold' : 'text-success fw-semibold'}>{formatAmount(viewingCounterpart)}</div>
      </> : <div className="text-secondary">Loading transaction…</div>}</Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => navigate('/transactions')}>Close</Button></Modal.Footer>
    </Modal>
  </>;
}
