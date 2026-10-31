import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Form, Modal, Pagination, Spinner, Table } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { api, apiErrorMessage } from '../api';
import type { Account, Category, DuplicateCheck, StatementCsvCandidate, StatementCsvPreview, StatementImport, StatementImportCandidateChange, StatementImportConfirmationResult, StatementImportPage } from '../types';

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

type ReviewRow = StatementCsvCandidate & { selected: boolean; transactionType: string; categoryId: string | null; duplicateDecision: 'Skip' | 'ImportAnyway' | 'Unresolved' | null };
type ReviewFilter = 'All' | 'Valid' | 'Invalid' | 'Duplicates' | 'Selected' | 'Income' | 'Expenses' | 'Transfers';

function rowMessages(row: ReviewRow, accountCurrency: string, statementType: string = 'Bank'): string[] {
  const messages: string[] = [];
  if (!row.transactionDate || Number.isNaN(Date.parse(row.transactionDate)) || new Date(row.transactionDate).getUTCFullYear() < 1900 || new Date(row.transactionDate) > new Date(Date.now() + 86_400_000)) messages.push('Enter a valid transaction date no earlier than 1900 and no later than tomorrow.');
  if (!row.description.trim() || row.description.trim().length > 300) messages.push('Description is required and must be at most 300 characters.');
  if (row.amount === null || !Number.isFinite(row.amount) || row.amount === 0 || Math.abs(row.amount) >= 1_000_000_000_000_000 || Number(row.amount.toFixed(4)) !== row.amount) messages.push('Amount must be nonzero and have at most four decimal places.');
  if (statementType !== 'CreditCard' && row.amount !== null && row.transactionType === 'Expense' && row.amount >= 0) messages.push('Expenses must have a negative amount.');
  if (statementType !== 'CreditCard' && row.amount !== null && ['Income', 'Refund'].includes(row.transactionType) && row.amount <= 0) messages.push('Income and refunds must have a positive amount.');
  if (!['Expense', 'Income', 'Refund', 'Transfer'].includes(row.transactionType)) messages.push('Choose a transaction type.');
  if (row.currency && !/^[A-Z]{3}$/.test(row.currency)) messages.push('Currency must be a three-letter code.');
  if (row.currency && row.currency !== accountCurrency.toUpperCase()) messages.push(`CSV currency ${row.currency} does not match account currency ${accountCurrency}.`);
  return messages;
}

export default function StatementsPage() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedAccount, setSelectedAccount] = useState('');
  const [statementType, setStatementType] = useState<'Bank' | 'CreditCard'>('Bank');
  const [file, setFile] = useState<File | null>(null);
  const [draggingFile, setDraggingFile] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [statements, setStatements] = useState<StatementImportPage>(emptyPage);
  const [pageNumber, setPageNumber] = useState(1);
  const [loading, setLoading] = useState(true);
  const [loadingAccounts, setLoadingAccounts] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [deletingId, setDeletingId] = useState('');
  const [parsingId, setParsingId] = useState('');
  const [csvPreview, setCsvPreview] = useState<{ statement: StatementImport; result: StatementCsvPreview; rows: ReviewRow[] } | null>(null);
  const [reviewFilter, setReviewFilter] = useState<ReviewFilter>('All');
  const [reviewSort, setReviewSort] = useState<'date' | 'description' | 'amount' | 'category'>('date');
  const [confirming, setConfirming] = useState(false);
  const [confirmationOpen, setConfirmationOpen] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [historySearch, setHistorySearch] = useState('');
  const [historyType, setHistoryType] = useState('All');
  const [historyAccount, setHistoryAccount] = useState('All');
  const [historyStatus, setHistoryStatus] = useState('All');
  const [historySort, setHistorySort] = useState<'newest' | 'oldest'>('newest');
  const [detailStatement, setDetailStatement] = useState<StatementImport | null>(null);
  const [detailsOpen, setDetailsOpen] = useState(false);

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

  useEffect(() => {
    api.get<Category[]>('/api/categories').then(response => setCategories(response.data)).catch(loadError => setError(apiErrorMessage(loadError)));
  }, []);

  useEffect(() => { void loadStatements(1); }, [loadStatements]);

  async function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file || !selectedAccount) return;
    const formElement = event.currentTarget;
    setError('');
    setNotice('');
    setUploading(true);
    setUploadProgress(0);
    const form = new FormData();
    form.append('financialAccountId', selectedAccount);
    form.append('statementType', statementType);
    form.append('file', file);
    try {
      await api.post<StatementImport>('/api/statements', form, { onUploadProgress: event => setUploadProgress(event.total ? Math.round(event.loaded * 100 / event.total) : 0) });
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

  function chooseFile(nextFile: File | undefined) {
    setError('');
    if (!nextFile) return;
    if (!nextFile.name.toLowerCase().endsWith('.csv') || (nextFile.type && nextFile.type !== 'text/csv' && nextFile.type !== 'application/vnd.ms-excel')) {
      setFile(null);
      setError('Choose a CSV file.');
      return;
    }
    setFile(nextFile);
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

  async function viewDetails(statement: StatementImport) {
    try {
      const response = await api.get<StatementImport>(`/api/statements/${statement.id}`);
      setDetailStatement(response.data);
      setDetailsOpen(true);
    } catch (detailError) {
      setError(apiErrorMessage(detailError));
    }
  }

  async function parseCsv(statement: StatementImport) {
    setError('');
    setNotice('');
    setCsvPreview(null);
    setParsingId(statement.id);
    try {
      const response = await api.post<StatementCsvPreview>(`/api/statements/${statement.id}/parse-csv`);
      const account = accounts.find(candidate => candidate.id === statement.financialAccountId);
      const rows = response.data.candidates.map(candidate => ({
        ...candidate,
        selected: false,
        transactionType: account?.accountType === 'Investment' ? '' : statement.statementType === 'Bank' ? (candidate.amount !== null && candidate.amount > 0 ? 'Income' : candidate.amount !== null && candidate.amount < 0 ? 'Expense' : '') : (candidate.amount !== null && candidate.amount > 0 ? 'Expense' : ''),
        categoryId: null,
        duplicateDecision: candidate.duplicateCheck?.classification === 'LikelyDuplicate' || candidate.duplicateCheck?.classification === 'PossibleDuplicate' ? 'Unresolved' as const : null
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
  const normalizedAmount = (row: ReviewRow): number | null => {
    if (row.amount === null || !row.transactionType) return null;
    if (csvPreview?.statement.statementType === 'CreditCard') {
      if (row.transactionType === 'Expense') return -Math.abs(row.amount);
      if (row.transactionType === 'Income' || row.transactionType === 'Refund') return Math.abs(row.amount);
      return row.amount;
    }
    return row.amount;
  };
  const checkedRows = csvPreview?.rows.filter(row => rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview.statement.statementType).length === 0) ?? [];
  const invalidRows = csvPreview?.rows.filter(row => rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview.statement.statementType).length > 0) ?? [];
  const eligibleRows = csvPreview?.rows.filter(row => rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview.statement.statementType).length === 0 && row.duplicateDecision !== 'Skip' && row.duplicateDecision !== 'Unresolved') ?? [];
  const selectedRows = eligibleRows.filter(row => row.selected);
  const duplicateRows = csvPreview?.rows.filter(row => row.duplicateCheck?.classification === 'LikelyDuplicate' || row.duplicateCheck?.classification === 'PossibleDuplicate') ?? [];
  const filteredRows = (csvPreview?.rows ?? []).filter(row => {
    switch (reviewFilter) {
      case 'Valid': return rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview?.statement.statementType).length === 0;
      case 'Invalid': return rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview?.statement.statementType).length > 0;
      case 'Duplicates': return row.duplicateCheck?.classification === 'LikelyDuplicate' || row.duplicateCheck?.classification === 'PossibleDuplicate';
      case 'Selected': return row.selected;
      case 'Income': return row.transactionType === 'Income' || row.transactionType === 'Refund';
      case 'Expenses': return row.transactionType === 'Expense';
      case 'Transfers': return row.transactionType === 'Transfer';
      default: return true;
    }
  }).slice().sort((a, b) => reviewSort === 'date' ? (a.transactionDate ?? '').localeCompare(b.transactionDate ?? '') : reviewSort === 'description' ? a.description.localeCompare(b.description) : reviewSort === 'category' ? (categories.find(c => c.id === a.categoryId)?.name ?? '').localeCompare(categories.find(c => c.id === b.categoryId)?.name ?? '') : (a.amount ?? 0) - (b.amount ?? 0));
  const currencyTotals = new Map<string, { income: number; expenses: number }>();
  for (const row of csvPreview?.rows ?? []) {
    if (!row.selected || row.transactionType === 'Transfer' || rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview?.statement.statementType).length > 0) continue;
    const currency = row.currency ?? statementAccount?.currency ?? 'CAD';
    const totals = currencyTotals.get(currency) ?? { income: 0, expenses: 0 };
    const amount = normalizedAmount(row) ?? 0;
    if (row.transactionType === 'Income' || row.transactionType === 'Refund') totals.income += amount;
    if (row.transactionType === 'Expense') totals.expenses += Math.abs(amount);
    currencyTotals.set(currency, totals);
  }
  const excludedCount = checkedRows.length - selectedRows.length;
  const unresolvedRows = csvPreview?.rows.filter(row =>
    (row.duplicateCheck?.classification === 'LikelyDuplicate' || row.duplicateCheck?.classification === 'PossibleDuplicate') && row.duplicateDecision === 'Unresolved') ?? [];
  const explicitlySkippedEveryCandidate = !!csvPreview && csvPreview.rows.length > 0 && csvPreview.rows.every(row =>
    (row.duplicateCheck?.classification === 'LikelyDuplicate' || row.duplicateCheck?.classification === 'PossibleDuplicate') && row.duplicateDecision === 'Skip');
  const canFinalizeImport = selectedRows.length > 0 || explicitlySkippedEveryCandidate;

  function setAllRowsSelected(selected: boolean) {
    setCsvPreview(current => current ? ({ ...current, rows: current.rows.map(row => {
      const valid = rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview?.statement.statementType).length === 0;
      const flagged = row.duplicateCheck?.classification === 'LikelyDuplicate' || row.duplicateCheck?.classification === 'PossibleDuplicate';
      if (!valid || row.duplicateDecision === 'Skip' || (flagged && row.duplicateDecision !== 'ImportAnyway')) return row;
      return { ...row, selected, duplicateDecision: flagged && !selected ? 'Unresolved' : row.duplicateDecision };
    }) }) : current);
  }

  function applyCategoryToSelected(categoryId: string) {
    if (selectedRows.length === 0) return;
    const selectedCategoryId = categoryId === '__none' || categoryId === '' ? null : categoryId;
    const numbers = new Set(selectedRows.map(row => row.sourceRowNumber));
    setCsvPreview(current => current ? ({ ...current, rows: current.rows.map(row => numbers.has(row.sourceRowNumber) ? { ...row, categoryId: selectedCategoryId } : row) }) : current);
    setNotice(`Category applied to ${selectedRows.length} selected transaction${selectedRows.length === 1 ? '' : 's'}.`);
  }

  async function confirmImport() {
    if (!csvPreview || !canFinalizeImport) return;
    setError(''); setNotice(''); setConfirming(true);
    const candidates: StatementImportCandidateChange[] = csvPreview.rows.map(row => ({
      sourceRowNumber: row.sourceRowNumber, selected: row.selected,
      transactionDate: row.transactionDate, description: row.description,
      subDescription: row.subDescription ?? null,
      amount: row.amount, transactionType: row.transactionType, categoryId: row.categoryId,
      duplicateDecision: row.duplicateDecision,
      reviewedDuplicateTransactionId: row.duplicateCheck?.existingTransactionId ?? null,
      reviewedDuplicateClassification: row.duplicateCheck?.classification ?? null
    }));
    try {
      const response = await api.post<StatementImportConfirmationResult>(`/api/statements/${csvPreview.statement.id}/confirm-import`, { candidates });
      setConfirmationOpen(false); setCsvPreview(null);
      setNotice(`Imported ${response.data.createdCount} transaction${response.data.createdCount === 1 ? '' : 's'}; ${response.data.excludedCount} valid row${response.data.excludedCount === 1 ? '' : 's'} excluded.`);
      await loadStatements(pageNumber);
    } catch (importError) {
      const details = (importError as { response?: { data?: StatementImportConfirmationResult } }).response?.data;
      if (details?.rejectedRows?.length) {
        setCsvPreview(current => current ? ({ ...current, rows: current.rows.map(row => {
          const rejected = details.rejectedRows.find(item => item.sourceRowNumber === row.sourceRowNumber);
          if (!rejected?.duplicateCheck) return row;
          const check: DuplicateCheck = rejected.duplicateCheck;
          const match = check.classification === 'LikelyDuplicate' || check.classification === 'PossibleDuplicate';
          return { ...row, duplicateCheck: check, duplicateDecision: match ? 'Unresolved' : null, selected: false };
        }) }) : current);
      }
      setError(apiErrorMessage(importError));
    } finally { setConfirming(false); }
  }

  const canUpload = !loadingAccounts && accounts.length > 0 && !!selectedAccount && !!file;
  const visibleStatements = statements.items.filter(statement => {
    const account = accounts.find(item => item.id === statement.financialAccountId);
    const search = historySearch.trim().toLowerCase();
    return (!search || statement.originalFileName.toLowerCase().includes(search) || (account?.institution ?? '').toLowerCase().includes(search)) &&
      (historyType === 'All' || statement.statementType === historyType) &&
      (historyAccount === 'All' || statement.financialAccountId === historyAccount) &&
      (historyStatus === 'All' || statement.status === historyStatus);
  }).slice().sort((a, b) => historySort === 'newest' ? b.uploadedAtUtc.localeCompare(a.uploadedAtUtc) : a.uploadedAtUtc.localeCompare(b.uploadedAtUtc));
  const displayStatus = (status: string) => status === 'Processed' ? 'Imported' : status;

  return <>
    <div className="page-heading">
      <div><div className="eyebrow">PRIVATE FILE STORAGE</div><h1>Statements</h1><p className="text-secondary mb-0">Upload statements and keep them with the account they belong to.</p></div>
    </div>
    {error && <Alert variant="danger" dismissible onClose={() => setError('')}>{error}</Alert>}
    {notice && <Alert variant="success" dismissible onClose={() => setNotice('')}>{notice}</Alert>}

    <div className="content-card p-3 p-md-4 mb-4">
      <div className="d-flex align-items-start justify-content-between flex-wrap gap-2 mb-3"><div><div className="eyebrow">IMPORT WORKFLOW</div><h2 className="section-title mb-1">Upload a statement</h2><p className="text-secondary mb-0">Choose the statement type and account, then upload your CSV to review transactions.</p></div><Badge bg="light" text="dark" className="border">1 Select · 2 Upload · 3 Review</Badge></div>
      {loadingAccounts ? <div className="py-3"><Spinner animation="border" size="sm" aria-label="Loading accounts" /></div> : accounts.length === 0 ?
        <Alert variant="info" className="mb-0">Create a financial account before uploading a statement. <Link to="/accounts">Go to accounts</Link>.</Alert> :
        <Form onSubmit={event => void upload(event)}>
          <div className="row g-3 align-items-end">
            <Form.Group className="col-md-4"><Form.Label>Statement type</Form.Label><Form.Select required value={statementType} onChange={event => setStatementType(event.target.value as 'Bank' | 'CreditCard')}><option value="Bank">Bank statement</option><option value="CreditCard">Credit card statement</option></Form.Select></Form.Group>
            <Form.Group className="col-md-4"><Form.Label>Financial account</Form.Label><Form.Select required value={selectedAccount} onChange={event => setSelectedAccount(event.target.value)}>
              {accounts.map(account => <option key={account.id} value={account.id}>{account.name} · {account.institution}</option>)}
            </Form.Select></Form.Group>
            <div className="col-md-4">
              <Form.Label htmlFor="statement-file">CSV file</Form.Label>
              <div className={`border rounded-3 p-3 text-center ${draggingFile ? 'border-primary bg-light' : 'bg-body-tertiary'}`} onDragOver={event => { event.preventDefault(); setDraggingFile(true); }} onDragLeave={() => setDraggingFile(false)} onDrop={event => { event.preventDefault(); setDraggingFile(false); chooseFile(event.dataTransfer.files[0]); }}>
                <div className="fw-semibold">Drop a CSV here</div><div className="small text-secondary mb-2">or browse your files</div>
                <Form.Control id="statement-file" className="visually-hidden" type="file" accept=".csv,text/csv,application/vnd.ms-excel" onChange={event => chooseFile((event.currentTarget as HTMLInputElement).files?.[0])} />
                <Button type="button" variant="outline-primary" size="sm" onClick={() => document.getElementById('statement-file')?.click()} disabled={uploading}>Browse CSV</Button>
                {file && <div className="d-flex align-items-center justify-content-center gap-2 mt-2 small"><span>{file.name} · {formatFileSize(file.size)}</span><Button type="button" variant="link" size="sm" className="p-0" onClick={() => setFile(null)} disabled={uploading}>Remove</Button></div>}
              </div>
            </div>
            <div className="col-12 d-flex justify-content-end"><Button type="submit" variant="dark" disabled={!canUpload || uploading}>{uploading ? <><Spinner size="sm" className="me-2" />Uploading {uploadProgress}%</> : 'Upload CSV'}</Button></div>
            {uploading && <div className="col-12"><div className="progress" role="progressbar" aria-label="Upload progress" aria-valuenow={uploadProgress} aria-valuemin={0} aria-valuemax={100}><div className="progress-bar" style={{ width: `${uploadProgress}%` }} /></div></div>}
          </div>
        </Form>}
    </div>

    <div className="content-card p-3 p-md-4">
      <div className="d-flex justify-content-between align-items-center mb-3"><div><h2 className="section-title">Statement history</h2><p className="text-secondary small mb-0">{statements.totalCount} uploaded file{statements.totalCount === 1 ? '' : 's'}</p></div><Button variant="outline-secondary" size="sm" onClick={() => void loadStatements(pageNumber)} disabled={loading}>Refresh</Button></div>
      <div className="row g-2 mb-3"><Form.Group className="col-12 col-md-4"><Form.Label>Search</Form.Label><Form.Control value={historySearch} onChange={event => setHistorySearch(event.target.value)} placeholder="Filename or institution" /></Form.Group><Form.Group className="col-6 col-md-2"><Form.Label>Type</Form.Label><Form.Select value={historyType} onChange={event => setHistoryType(event.target.value)}><option>All</option><option value="Bank">Bank</option><option value="CreditCard">Credit Card</option></Form.Select></Form.Group><Form.Group className="col-6 col-md-2"><Form.Label>Account</Form.Label><Form.Select value={historyAccount} onChange={event => setHistoryAccount(event.target.value)}><option>All</option>{accounts.map(account => <option value={account.id} key={account.id}>{account.name}</option>)}</Form.Select></Form.Group><Form.Group className="col-6 col-md-2"><Form.Label>Status</Form.Label><Form.Select value={historyStatus} onChange={event => setHistoryStatus(event.target.value)}><option>All</option>{['Uploaded','Processing','Processed','Failed','Deleting'].map(status => <option key={status}>{status}</option>)}</Form.Select></Form.Group><Form.Group className="col-6 col-md-2"><Form.Label>Upload date</Form.Label><Form.Select value={historySort} onChange={event => setHistorySort(event.target.value as 'newest' | 'oldest')}><option value="newest">Newest first</option><option value="oldest">Oldest first</option></Form.Select></Form.Group></div>
      {loading ? <div className="text-center py-5"><Spinner animation="border" size="sm" aria-label="Loading statements" /></div> : statements.items.length === 0 ?
        <div className="empty-state"><div className="empty-icon">↑</div><h3>No statements uploaded yet</h3><p className="text-secondary">Choose a statement type, account, and CSV above to get started.</p></div> : visibleStatements.length === 0 ? <Alert variant="light" className="mb-0">No statements match these filters.</Alert> : <>
        <div className="d-none d-lg-block"><Table responsive hover className="align-middle mb-0"><thead><tr><th>Statement</th><th>Account / institution</th><th>Type</th><th>Uploaded</th><th>Size</th><th>Imported</th><th>Status</th><th className="text-end">Actions</th></tr></thead><tbody>{visibleStatements.map(statement => { const account = accounts.find(item => item.id === statement.financialAccountId); return <tr key={statement.id}><td><strong>{statement.originalFileName}</strong></td><td>{statement.accountName}<div className="small text-secondary">{account?.institution ?? '—'}</div></td><td>{statement.statementType === 'CreditCard' ? 'Credit card' : 'Bank'}</td><td>{new Date(statement.uploadedAtUtc).toLocaleString()}</td><td>{formatFileSize(statement.fileSizeBytes)}</td><td>{statement.importedTransactionCount}</td><td><Badge bg={statusVariant(statement.status)}>{displayStatus(statement.status)}</Badge></td><td className="text-end text-nowrap"><Button size="sm" variant="outline-secondary" className="me-1" onClick={() => void viewDetails(statement)}>Details</Button>{statement.status === 'Uploaded' && statement.originalFileName.toLowerCase().endsWith('.csv') && <Button size="sm" variant="outline-primary" className="me-1" onClick={() => void parseCsv(statement)} disabled={!!parsingId || uploading || !!deletingId}>{parsingId === statement.id ? 'Parsing…' : 'Review'}</Button>}<Button size="sm" variant="outline-danger" onClick={() => void remove(statement)} disabled={!!parsingId || deletingId === statement.id}>{statement.status === 'Deleting' ? 'Retry delete' : 'Delete'}</Button></td></tr>; })}</tbody></Table></div>
        <div className="d-lg-none d-grid gap-2">{visibleStatements.map(statement => { const account = accounts.find(item => item.id === statement.financialAccountId); return <article className="border rounded-3 p-3" key={statement.id}><div className="d-flex justify-content-between gap-2"><strong className="text-break">{statement.originalFileName}</strong><Badge bg={statusVariant(statement.status)} className="align-self-start">{displayStatus(statement.status)}</Badge></div><div className="small text-secondary mt-2">{statement.accountName} · {account?.institution ?? 'Institution unavailable'}</div><div className="d-flex flex-wrap gap-2 small mt-2"><Badge bg="light" text="dark" className="border">{statement.statementType === 'CreditCard' ? 'Credit card' : 'Bank'}</Badge><span>{new Date(statement.uploadedAtUtc).toLocaleDateString()}</span><span>{formatFileSize(statement.fileSizeBytes)}</span><span>{statement.importedTransactionCount} imported</span></div><div className="d-flex flex-wrap gap-2 mt-3"><Button size="sm" variant="outline-secondary" onClick={() => void viewDetails(statement)}>Details</Button>{statement.status === 'Uploaded' && statement.originalFileName.toLowerCase().endsWith('.csv') && <Button size="sm" variant="outline-primary" onClick={() => void parseCsv(statement)} disabled={!!parsingId || uploading || !!deletingId}>Review</Button>}<Button size="sm" variant="outline-danger" onClick={() => void remove(statement)} disabled={!!parsingId || deletingId === statement.id}>{statement.status === 'Deleting' ? 'Retry delete' : 'Delete'}</Button></div></article>; })}</div></>}
      {csvPreview && <section className="mt-4" aria-label="CSV import review">
        <div className="d-flex justify-content-between align-items-start gap-3 mb-3">
          <div><h3 className="section-title mb-1">Review: {csvPreview.statement.originalFileName}</h3><p className="text-secondary small mb-0">Correct rows, choose which valid rows to import, then confirm. Nothing is saved until confirmation.</p></div>
          <Button variant="outline-secondary" size="sm" onClick={() => setCsvPreview(null)}>Close preview</Button>
        </div>
        <div className="d-flex gap-2 flex-wrap mb-3">
          <Badge bg="dark">Detected: {csvPreview.rows.length}</Badge><Badge bg="success">Valid: {checkedRows.length}</Badge><Badge bg="danger">Invalid: {invalidRows.length}</Badge><Badge bg="primary">Selected: {selectedRows.length}</Badge><Badge bg="warning" text="dark">Potential duplicates: {duplicateRows.length}</Badge>
        </div>
        <div className="row g-2 mb-3">{[...currencyTotals.entries()].map(([currency, totals]) => <div className="col-12 col-md-6" key={currency}><div className="border rounded p-2 d-flex justify-content-between"><span>{currency} · Income</span><strong className="text-success">{totals.income.toFixed(2)}</strong><span>Expenses</span><strong className="text-danger">{totals.expenses.toFixed(2)}</strong></div></div>)}</div>
        <div className="row g-2 align-items-end mb-3"><Form.Group className="col-12 col-md-5"><Form.Label>Filter transactions</Form.Label><Form.Select value={reviewFilter} onChange={event => setReviewFilter(event.target.value as ReviewFilter)}>{(['All','Valid','Invalid','Duplicates','Selected','Income','Expenses','Transfers'] as ReviewFilter[]).map(filter => <option key={filter}>{filter}</option>)}</Form.Select></Form.Group><Form.Group className="col-12 col-md-4"><Form.Label>Sort by</Form.Label><Form.Select value={reviewSort} onChange={event => setReviewSort(event.target.value as typeof reviewSort)}><option value="date">Date</option><option value="description">Description</option><option value="amount">Amount</option><option value="category">Category</option></Form.Select></Form.Group><div className="col-md-3 text-md-end small text-secondary">Showing {filteredRows.length} of {csvPreview.rows.length}</div></div>
        <div className="d-flex flex-wrap align-items-end gap-2 mb-3 p-2 rounded bg-body-tertiary">
          <Form.Check type="checkbox" label={`Select all eligible (${eligibleRows.length})`} checked={eligibleRows.length > 0 && eligibleRows.every(row => row.selected)} disabled={confirming || eligibleRows.length === 0} onChange={event => setAllRowsSelected(event.currentTarget.checked)} />
          <Button size="sm" variant="outline-secondary" disabled={confirming || selectedRows.length === 0} onClick={() => setAllRowsSelected(false)}>Deselect all</Button>
          <Form.Select aria-label="Apply category to selected transactions" size="sm" className="w-auto" value="" disabled={confirming || selectedRows.length === 0} onChange={event => applyCategoryToSelected(event.currentTarget.value)}>
            <option value="">Apply category to selected…</option><option value="__none">Uncategorized</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}
          </Form.Select>
        </div>
        {csvPreview.rows.length === 0 ? <p className="text-secondary">The CSV contains no transaction rows.</p> :
          <Table responsive bordered size="sm" className="align-middle mb-0">
            <thead><tr><th>Select</th><th>Source row</th><th>Transaction date</th><th>Description</th><th>Sub-description</th><th>Amount</th><th>Type</th><th>Category</th><th>Currency / reference</th><th>Status / duplicate review</th></tr></thead>
            <tbody>{filteredRows.map(row => {
              const messages = rowMessages(row, statementAccount?.currency ?? 'CAD', csvPreview.statement.statementType);
              const valid = messages.length === 0;
              const normalized = normalizedAmount(row);
              const typeTone = row.transactionType === 'Expense' ? 'danger' : row.transactionType === 'Income' || row.transactionType === 'Refund' ? 'success' : row.transactionType === 'Transfer' ? 'info' : 'secondary';
              return <tr key={row.sourceRowNumber} className={!valid ? 'table-warning' : undefined}>
              <td>{row.duplicateCheck && row.duplicateCheck.classification !== 'NoMatch' ?
                <Form.Select aria-label={`Duplicate decision for row ${row.sourceRowNumber}`} size="sm" disabled={confirming} value={row.duplicateDecision ?? 'Unresolved'} onChange={event => {
                  const decision = event.currentTarget.value as ReviewRow['duplicateDecision'];
                  editRow(row.sourceRowNumber, { duplicateDecision: decision, selected: decision === 'ImportAnyway' });
                }}><option value="Unresolved">Leave unresolved</option><option value="Skip">Skip candidate</option><option value="ImportAnyway">Import anyway</option></Form.Select> :
                <Form.Check aria-label={`Select source row ${row.sourceRowNumber}`} checked={row.selected && valid} disabled={!valid || confirming || row.duplicateDecision === 'Skip'} onChange={event => editRow(row.sourceRowNumber, { selected: event.currentTarget.checked })} />}</td>
              <td>{row.sourceRowNumber}</td>
              <td><Form.Control aria-label={`Transaction date row ${row.sourceRowNumber}`} type="date" size="sm" value={row.transactionDate ?? ''} onChange={event => editRow(row.sourceRowNumber, { transactionDate: event.currentTarget.value || null })} />{row.postedDate && <div className="small text-secondary mt-1">Posted: {row.postedDate}</div>}</td>
              <td><Form.Control aria-label={`Description row ${row.sourceRowNumber}`} size="sm" value={row.description} onChange={event => editRow(row.sourceRowNumber, { description: event.currentTarget.value })} />{row.referenceNumber && <div className="small text-secondary mt-1">Ref: {row.referenceNumber}</div>}</td>
              <td><Form.Control aria-label={`Sub-description row ${row.sourceRowNumber}`} size="sm" value={row.subDescription ?? ''} onChange={event => editRow(row.sourceRowNumber, { subDescription: event.currentTarget.value || null })} /></td>
              <td><div className="small text-secondary">Source: {row.sourceAmount ?? row.amount ?? '—'}</div><Form.Control aria-label={`Amount row ${row.sourceRowNumber}`} type="number" step="0.0001" size="sm" value={row.amount ?? ''} onChange={event => editRow(row.sourceRowNumber, { amount: event.currentTarget.value === '' ? null : Number(event.currentTarget.value) })} /><div className={`small fw-semibold text-${typeTone}`}>Normalized: {normalized === null ? 'Choose type' : normalized.toFixed(2)}</div></td>
              <td><Form.Select aria-label={`Type row ${row.sourceRowNumber}`} size="sm" value={row.transactionType} onChange={event => editRow(row.sourceRowNumber, { transactionType: event.currentTarget.value })}><option value="">Choose type</option><option>Expense</option><option>Income</option><option>Refund</option><option>Transfer</option></Form.Select><Badge bg={typeTone} className="mt-1">{row.transactionType || 'Review type'}</Badge></td>
              <td><Form.Select aria-label={`Category row ${row.sourceRowNumber}`} size="sm" value={row.categoryId ?? ''} disabled={!valid || confirming || row.duplicateDecision === 'Skip'} onChange={event => editRow(row.sourceRowNumber, { categoryId: event.currentTarget.value || null })}><option value="">Uncategorized</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}</Form.Select></td>
              <td>{row.currency ?? statementAccount?.currency ?? '—'}{row.referenceNumber && <div className="small text-secondary">{row.referenceNumber}</div>}</td>
              <td><Badge bg={valid ? row.selected ? 'primary' : 'secondary' : 'danger'}>{valid ? row.selected ? 'Selected' : 'Excluded' : 'Invalid'}</Badge>
                {messages.length > 0 && <ul className="small text-danger mb-0 mt-1">{messages.map((message, index) => <li key={`${row.sourceRowNumber}-${index}`}>{message}</li>)}</ul>}
                {row.duplicateCheck && <div className={`small mt-1 ${row.duplicateCheck.classification === 'NoMatch' ? 'text-secondary' : 'text-danger'}`}>
                  <strong>{row.duplicateCheck.classification}</strong>: {row.duplicateCheck.reason}
                  {row.duplicateCheck.existingTransactionId && <div className="border rounded p-2 mt-1 bg-light">
                    <div>{row.duplicateCheck.existingTransactionDate} · {row.duplicateCheck.existingDescription}</div>
                    <div>{row.duplicateCheck.existingAmount?.toFixed(2)} {row.duplicateCheck.existingCurrency} · {row.duplicateCheck.existingAccountName}</div>
                  </div>}
                </div>}
              </td>
            </tr>})}</tbody>
          </Table>}
        {unresolvedRows.length > 0 && <Alert variant="warning" className="mt-3 mb-0">Resolve {unresolvedRows.length} flagged row{unresolvedRows.length === 1 ? '' : 's'} by choosing Skip candidate or Import anyway. Unresolved rows block confirmation.</Alert>}
        <div className="d-flex justify-content-end gap-2 mt-3"><Button variant="outline-secondary" onClick={() => setCsvPreview(null)} disabled={confirming}>Cancel</Button><Button variant="dark" disabled={!canFinalizeImport || unresolvedRows.length > 0 || confirming} onClick={() => setConfirmationOpen(true)}>Review import ({selectedRows.length})</Button></div>
      </section>}
      {statements.totalPages > 1 && <Pagination className="justify-content-center mt-3 mb-0">
        <Pagination.Prev disabled={pageNumber <= 1 || loading} onClick={() => void loadStatements(pageNumber - 1)} />
        <Pagination.Item active>{pageNumber} / {statements.totalPages}</Pagination.Item>
        <Pagination.Next disabled={pageNumber >= statements.totalPages || loading} onClick={() => void loadStatements(pageNumber + 1)} />
      </Pagination>}
    </div>
    <Modal show={detailsOpen} onHide={() => setDetailsOpen(false)} centered><Modal.Header closeButton><Modal.Title>Statement details</Modal.Title></Modal.Header><Modal.Body>{detailStatement && <dl className="row mb-0"><dt className="col-sm-4">Filename</dt><dd className="col-sm-8 text-break">{detailStatement.originalFileName}</dd><dt className="col-sm-4">Type</dt><dd className="col-sm-8">{detailStatement.statementType === 'CreditCard' ? 'Credit card statement' : 'Bank statement'}</dd><dt className="col-sm-4">Account</dt><dd className="col-sm-8">{detailStatement.accountName}</dd><dt className="col-sm-4">Uploaded</dt><dd className="col-sm-8">{new Date(detailStatement.uploadedAtUtc).toLocaleString()}</dd><dt className="col-sm-4">Status</dt><dd className="col-sm-8"><Badge bg={statusVariant(detailStatement.status)}>{displayStatus(detailStatement.status)}</Badge></dd><dt className="col-sm-4">Transactions imported</dt><dd className="col-sm-8">{detailStatement.importedTransactionCount}</dd><dt className="col-sm-4">File size</dt><dd className="col-sm-8">{formatFileSize(detailStatement.fileSizeBytes)}</dd></dl>}</Modal.Body><Modal.Footer><Button variant="secondary" onClick={() => setDetailsOpen(false)}>Close</Button></Modal.Footer></Modal>
    <Modal show={confirmationOpen} onHide={() => setConfirmationOpen(false)} centered>
      <Modal.Header closeButton><Modal.Title>Confirm statement import</Modal.Title></Modal.Header>
      <Modal.Body><p>{selectedRows.length === 0 ? 'Finalize this statement without creating transactions?' : <>Import {selectedRows.length} selected transaction{selectedRows.length === 1 ? '' : 's'} into <strong>{csvPreview?.statement.accountName}</strong>?</>}</p><p className="mb-0 text-secondary">{excludedCount} valid row{excludedCount === 1 ? '' : 's'} will be excluded. {invalidRows.length} invalid row{invalidRows.length === 1 ? '' : 's'} will not be imported. Duplicate warnings marked “Import anyway” will be overridden. Imported rows will appear in Transactions and the dashboard.</p></Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => setConfirmationOpen(false)} disabled={confirming}>Back to review</Button><Button variant="dark" onClick={() => void confirmImport()} disabled={confirming}>{confirming ? <><Spinner size="sm" className="me-2" />Importing…</> : selectedRows.length === 0 ? 'Confirm skipped rows' : `Confirm ${selectedRows.length} transactions`}</Button></Modal.Footer>
    </Modal>
  </>;
}
