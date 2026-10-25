import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Form, Modal, Pagination, Spinner, Table } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { api, apiErrorMessage } from '../api';
import type { Account, StatementCsvCandidate, StatementCsvPreview, StatementImport, StatementImportCandidateChange, StatementImportConfirmationResult, StatementImportPage } from '../types';

const pageSize = 25;
const emptyPage: StatementImportPage = { items: [], page: 1, pageSize, totalCount: 0, totalPages: 0 };

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
}

function statusVariant(status: string): 'secondary' | 'warning' | 'success' | 'danger' {
  switch (status.toLowerCase()) {
    case 'processed': return 'success';
    case 'failed': return 'danger';
    case 'deleting': return 'warning';
    default: return 'secondary';
  }
}

type ReviewRow = StatementCsvCandidate & { selected: boolean; transactionType: string };

function rowMessages(row: ReviewRow, accountCurrency: string): string[] {
  const messages: string[] = [];
  if (!row.transactionDate || Number.isNaN(Date.parse(row.transactionDate)) || new Date(row.transactionDate).getUTCFullYear() < 1900 || new Date(row.transactionDate) > new Date(Date.now() + 86_400_000)) messages.push('Enter a valid transaction date no earlier than 1900 and no later than tomorrow.');
  if (!row.description.trim() || row.description.trim().length > 300) messages.push('Description is required and must be at most 300 characters.');
  if (row.amount === null || !Number.isFinite(row.amount) || row.amount === 0 || Math.abs(row.amount) >= 1_000_000_000_000_000 || Number(row.amount.toFixed(4)) !== row.amount) messages.push('Amount must be nonzero and have at most four decimal places.');
  if (row.amount !== null && row.transactionType === 'Expense' && row.amount >= 0) messages.push('Expenses must have a negative amount.');
  if (row.amount !== null && ['Income', 'Refund'].includes(row.transactionType) && row.amount <= 0) messages.push('Income and refunds must have a positive amount.');
  if (!['Expense', 'Income', 'Refund', 'Transfer'].includes(row.transactionType)) messages.push('Choose a transaction type.');
  if (row.currency && !/^[A-Z]{3}$/.test(row.currency)) messages.push('Currency must be a three-letter code.');
  if (row.currency && row.currency !== accountCurrency.toUpperCase()) messages.push(`CSV currency ${row.currency} does not match account currency ${accountCurrency}.`);
  return messages;
}

export default function StatementsPage() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [selectedAccount, setSelectedAccount] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [statements, setStatements] = useState<StatementImportPage>(emptyPage);
  const [pageNumber, setPageNumber] = useState(1);
  const [loading, setLoading] = useState(true);
  const [loadingAccounts, setLoadingAccounts] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [deletingId, setDeletingId] = useState('');
  const [parsingId, setParsingId] = useState('');
  const [csvPreview, setCsvPreview] = useState<{ statement: StatementImport; result: StatementCsvPreview; rows: ReviewRow[] } | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [confirmationOpen, setConfirmationOpen] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const loadStatements = useCallback(async (page: number) => {
    setLoading(true);
    try {
      const response = await api.get<StatementImportPage>('/api/statements', { params: { page, pageSize } });
      setStatements(response.data);
      setPageNumber(response.data.page);
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    api.get<Account[]>('/api/accounts')
      .then(response => {
        if (!active) return;
        setAccounts(response.data);
        setSelectedAccount(response.data[0]?.id ?? '');
      })
      .catch(loadError => { if (active) setError(apiErrorMessage(loadError)); })
      .finally(() => { if (active) setLoadingAccounts(false); });
    return () => { active = false; };
  }, []);

  useEffect(() => { void loadStatements(1); }, [loadStatements]);

  async function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file || !selectedAccount) return;
    const formElement = event.currentTarget;
    setError('');
    setNotice('');
    setUploading(true);
    const form = new FormData();
    form.append('financialAccountId', selectedAccount);
    form.append('file', file);
    try {
      await api.post<StatementImport>('/api/statements', form);
      setFile(null);
      formElement.reset();
      setNotice('Statement uploaded. Its contents have not been imported or parsed.');
      await loadStatements(1);
    } catch (uploadError) {
      setError(apiErrorMessage(uploadError));
    } finally {
      setUploading(false);
    }
  }

  async function remove(statement: StatementImport) {
    if (!window.confirm(`Delete the uploaded file “${statement.originalFileName}”?`)) return;
    setError('');
    setNotice('');
    setDeletingId(statement.id);
    try {
      await api.delete(`/api/statements/${statement.id}`);
      setNotice('Statement deleted.');
      await loadStatements(pageNumber > 1 && statements.items.length === 1 ? pageNumber - 1 : pageNumber);
    } catch (deleteError) {
      setError(apiErrorMessage(deleteError));
    } finally {
      setDeletingId('');
    }
  }

  async function parseCsv(statement: StatementImport) {
    setError('');
    setNotice('');
    setCsvPreview(null);
    setParsingId(statement.id);
    try {
      const response = await api.post<StatementCsvPreview>(`/api/statements/${statement.id}/parse-csv`);
      const rows = response.data.candidates.map(candidate => ({
        ...candidate,
        selected: false,
        transactionType: ''
      }));
      setCsvPreview({ statement, result: response.data, rows });
    } catch (parseError) {
      setError(apiErrorMessage(parseError));
    } finally {
      setParsingId('');
    }
  }

  function editRow(sourceRowNumber: number, changes: Partial<ReviewRow>) {
    setCsvPreview(current => current ? ({ ...current, rows: current.rows.map(row => row.sourceRowNumber === sourceRowNumber ? { ...row, ...changes } : row) }) : current);
  }

  const statementAccount = csvPreview ? accounts.find(account => account.id === csvPreview.statement.financialAccountId) : undefined;
  const checkedRows = csvPreview?.rows.filter(row => rowMessages(row, statementAccount?.currency ?? 'CAD').length === 0) ?? [];
  const invalidRows = csvPreview?.rows.filter(row => rowMessages(row, statementAccount?.currency ?? 'CAD').length > 0) ?? [];
  const selectedRows = csvPreview?.rows.filter(row => row.selected && rowMessages(row, statementAccount?.currency ?? 'CAD').length === 0) ?? [];
  const excludedCount = checkedRows.length - selectedRows.length;

  async function confirmImport() {
    if (!csvPreview || selectedRows.length === 0) return;
    setError(''); setNotice(''); setConfirming(true);
    const candidates: StatementImportCandidateChange[] = csvPreview.rows.map(row => ({
      sourceRowNumber: row.sourceRowNumber, selected: row.selected,
      transactionDate: row.transactionDate, description: row.description,
      amount: row.amount, transactionType: row.transactionType, categoryId: null
    }));
    try {
      const response = await api.post<StatementImportConfirmationResult>(`/api/statements/${csvPreview.statement.id}/confirm-import`, { candidates });
      setConfirmationOpen(false); setCsvPreview(null);
      setNotice(`Imported ${response.data.createdCount} transaction${response.data.createdCount === 1 ? '' : 's'}; ${response.data.excludedCount} valid row${response.data.excludedCount === 1 ? '' : 's'} excluded.`);
      await loadStatements(pageNumber);
    } catch (importError) {
      setError(apiErrorMessage(importError));
    } finally { setConfirming(false); }
  }

  const canUpload = !loadingAccounts && accounts.length > 0 && !!selectedAccount && !!file;

  return <>
    <div className="page-heading">
      <div><div className="eyebrow">PRIVATE FILE STORAGE</div><h1>Statements</h1><p className="text-secondary mb-0">Upload statements and keep them with the account they belong to.</p></div>
    </div>
    {error && <Alert variant="danger" dismissible onClose={() => setError('')}>{error}</Alert>}
    {notice && <Alert variant="success" dismissible onClose={() => setNotice('')}>{notice}</Alert>}

    <div className="content-card p-3 p-md-4 mb-4">
      <h2 className="section-title mb-3">Upload a statement</h2>
      {loadingAccounts ? <div className="py-3"><Spinner animation="border" size="sm" aria-label="Loading accounts" /></div> : accounts.length === 0 ?
        <Alert variant="info" className="mb-0">Create a financial account before uploading a statement. <Link to="/accounts">Go to accounts</Link>.</Alert> :
        <Form onSubmit={event => void upload(event)}>
          <div className="row g-3 align-items-end">
            <Form.Group className="col-md-4"><Form.Label>Financial account</Form.Label><Form.Select required value={selectedAccount} onChange={event => setSelectedAccount(event.target.value)}>
              {accounts.map(account => <option key={account.id} value={account.id}>{account.name} · {account.institution}</option>)}
            </Form.Select></Form.Group>
            <Form.Group className="col-md-5"><Form.Label>Statement file</Form.Label><Form.Control type="file" required accept=".pdf,.csv,.xlsx,application/pdf,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={event => setFile((event.currentTarget as HTMLInputElement).files?.[0] ?? null)} /><Form.Text>PDF, CSV, or XLSX. CSV files can be preview-parsed after upload; no transactions are created.</Form.Text></Form.Group>
            <div className="col-md-3"><Button type="submit" variant="dark" className="w-100" disabled={!canUpload || uploading}>{uploading ? <><Spinner size="sm" className="me-2" />Uploading…</> : 'Upload statement'}</Button></div>
          </div>
        </Form>}
    </div>

    <div className="content-card p-3 p-md-4">
      <div className="d-flex justify-content-between align-items-center mb-3"><div><h2 className="section-title">Uploaded statements</h2><p className="text-secondary small mb-0">{statements.totalCount} file{statements.totalCount === 1 ? '' : 's'}</p></div><Button variant="outline-secondary" size="sm" onClick={() => void loadStatements(pageNumber)} disabled={loading}>Refresh</Button></div>
      {loading ? <div className="text-center py-5"><Spinner animation="border" size="sm" aria-label="Loading statements" /></div> : statements.items.length === 0 ?
        <div className="empty-state"><div className="empty-icon">↑</div><h3>No statements uploaded</h3><p className="text-secondary">Uploaded files will appear here. No transactions are created from them.</p></div> :
        <Table responsive hover className="align-middle mb-0">
          <thead><tr><th>File</th><th>Account</th><th>Uploaded</th><th>Size</th><th>Status</th><th className="text-end">Actions</th></tr></thead>
          <tbody>{statements.items.map(statement => <tr key={statement.id}>
            <td><strong>{statement.originalFileName}</strong><div className="small text-secondary">{statement.contentType}</div></td>
            <td>{statement.accountName}</td>
            <td>{new Date(statement.uploadedAtUtc).toLocaleString()}</td>
            <td>{formatFileSize(statement.fileSizeBytes)}</td>
            <td><Badge bg={statusVariant(statement.status)}>{statement.status}</Badge></td>
            <td className="text-end text-nowrap">
              {statement.originalFileName.toLowerCase().endsWith('.csv') && statement.status === 'Uploaded' &&
                <Button size="sm" variant="outline-primary" className="me-2" onClick={() => void parseCsv(statement)} disabled={!!parsingId || uploading || !!deletingId}>
                  {parsingId === statement.id ? <><Spinner size="sm" className="me-1" />Parsing…</> : 'Parse CSV'}
                </Button>}
              <Button size="sm" variant="outline-danger" onClick={() => void remove(statement)} disabled={!!parsingId || deletingId === statement.id}>{deletingId === statement.id ? 'Deleting…' : statement.status === 'Deleting' ? 'Retry delete' : 'Delete'}</Button>
            </td>
          </tr>)}</tbody>
        </Table>}
      {csvPreview && <section className="mt-4" aria-label="CSV import review">
        <div className="d-flex justify-content-between align-items-start gap-3 mb-3">
          <div><h3 className="section-title mb-1">Review: {csvPreview.statement.originalFileName}</h3><p className="text-secondary small mb-0">Correct rows, choose which valid rows to import, then confirm. Nothing is saved until confirmation.</p></div>
          <Button variant="outline-secondary" size="sm" onClick={() => setCsvPreview(null)}>Close preview</Button>
        </div>
        <div className="d-flex gap-2 flex-wrap mb-3">
          <Badge bg="primary">Selected: {selectedRows.length}</Badge><Badge bg="success">Valid: {checkedRows.length}</Badge><Badge bg="danger">Invalid: {invalidRows.length}</Badge><Badge bg="secondary">Excluded: {excludedCount}</Badge>
        </div>
        {csvPreview.rows.length === 0 ? <p className="text-secondary">The CSV contains no transaction rows.</p> :
          <Table responsive bordered size="sm" className="align-middle mb-0">
            <thead><tr><th>Import</th><th>Source row</th><th>Transaction date</th><th>Description</th><th>Amount</th><th>Type</th><th>Currency / reference</th><th>Status</th></tr></thead>
            <tbody>{csvPreview.rows.map(row => {
              const messages = rowMessages(row, statementAccount?.currency ?? 'CAD');
              const valid = messages.length === 0;
              return <tr key={row.sourceRowNumber} className={!valid ? 'table-warning' : undefined}>
              <td><Form.Check aria-label={`Select source row ${row.sourceRowNumber}`} checked={row.selected && valid} disabled={!valid || confirming} onChange={event => editRow(row.sourceRowNumber, { selected: event.currentTarget.checked })} /></td>
              <td>{row.sourceRowNumber}</td>
              <td><Form.Control aria-label={`Transaction date row ${row.sourceRowNumber}`} type="date" size="sm" value={row.transactionDate ?? ''} onChange={event => editRow(row.sourceRowNumber, { transactionDate: event.currentTarget.value || null })} />{row.postedDate && <div className="small text-secondary mt-1">Posted: {row.postedDate}</div>}</td>
              <td><Form.Control aria-label={`Description row ${row.sourceRowNumber}`} size="sm" value={row.description} onChange={event => editRow(row.sourceRowNumber, { description: event.currentTarget.value })} />{row.referenceNumber && <div className="small text-secondary mt-1">Ref: {row.referenceNumber}</div>}</td>
              <td><Form.Control aria-label={`Amount row ${row.sourceRowNumber}`} type="number" step="0.0001" size="sm" value={row.amount ?? ''} onChange={event => editRow(row.sourceRowNumber, { amount: event.currentTarget.value === '' ? null : Number(event.currentTarget.value) })} /></td>
              <td><Form.Select aria-label={`Type row ${row.sourceRowNumber}`} size="sm" value={row.transactionType} onChange={event => editRow(row.sourceRowNumber, { transactionType: event.currentTarget.value })}><option value="">Choose type</option><option>Expense</option><option>Income</option><option>Refund</option><option>Transfer</option></Form.Select></td>
              <td>{row.currency ?? statementAccount?.currency ?? '—'}{row.referenceNumber && <div className="small text-secondary">{row.referenceNumber}</div>}</td>
              <td><Badge bg={valid ? row.selected ? 'primary' : 'secondary' : 'danger'}>{valid ? row.selected ? 'Selected' : 'Excluded' : 'Invalid'}</Badge>
                {messages.length > 0 && <ul className="small text-danger mb-0 mt-1">{messages.map((message, index) => <li key={`${row.sourceRowNumber}-${index}`}>{message}</li>)}</ul>}
              </td>
            </tr>})}</tbody>
          </Table>}
        <div className="d-flex justify-content-end gap-2 mt-3"><Button variant="outline-secondary" onClick={() => setCsvPreview(null)} disabled={confirming}>Cancel</Button><Button variant="dark" disabled={selectedRows.length === 0 || confirming} onClick={() => setConfirmationOpen(true)}>Review import ({selectedRows.length})</Button></div>
      </section>}
      {statements.totalPages > 1 && <Pagination className="justify-content-center mt-3 mb-0">
        <Pagination.Prev disabled={pageNumber <= 1 || loading} onClick={() => void loadStatements(pageNumber - 1)} />
        <Pagination.Item active>{pageNumber} / {statements.totalPages}</Pagination.Item>
        <Pagination.Next disabled={pageNumber >= statements.totalPages || loading} onClick={() => void loadStatements(pageNumber + 1)} />
      </Pagination>}
    </div>
    <Modal show={confirmationOpen} onHide={() => setConfirmationOpen(false)} centered>
      <Modal.Header closeButton><Modal.Title>Confirm statement import</Modal.Title></Modal.Header>
      <Modal.Body><p>Import {selectedRows.length} selected transaction{selectedRows.length === 1 ? '' : 's'} into <strong>{csvPreview?.statement.accountName}</strong>?</p><p className="mb-0 text-secondary">{excludedCount} valid row{excludedCount === 1 ? '' : 's'} will be excluded. {invalidRows.length} invalid row{invalidRows.length === 1 ? '' : 's'} will not be imported. Imported rows will appear in Transactions and the dashboard.</p></Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => setConfirmationOpen(false)} disabled={confirming}>Back to review</Button><Button variant="dark" onClick={() => void confirmImport()} disabled={confirming}>{confirming ? <><Spinner size="sm" className="me-2" />Importing…</> : `Confirm ${selectedRows.length} transactions`}</Button></Modal.Footer>
    </Modal>
  </>;
}
