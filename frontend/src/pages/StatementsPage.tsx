import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Form, Pagination, Spinner, Table } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { api, apiErrorMessage } from '../api';
import type { Account, StatementImport, StatementImportPage } from '../types';

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
            <Form.Group className="col-md-5"><Form.Label>Statement file</Form.Label><Form.Control type="file" required accept=".pdf,.csv,.xlsx,application/pdf,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={event => setFile((event.currentTarget as HTMLInputElement).files?.[0] ?? null)} /><Form.Text>PDF, CSV, or XLSX. File contents are stored privately and are not parsed in this phase.</Form.Text></Form.Group>
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
            <td className="text-end"><Button size="sm" variant="outline-danger" onClick={() => void remove(statement)} disabled={deletingId === statement.id}>{deletingId === statement.id ? 'Deleting…' : statement.status === 'Deleting' ? 'Retry delete' : 'Delete'}</Button></td>
          </tr>)}</tbody>
        </Table>}
      {statements.totalPages > 1 && <Pagination className="justify-content-center mt-3 mb-0">
        <Pagination.Prev disabled={pageNumber <= 1 || loading} onClick={() => void loadStatements(pageNumber - 1)} />
        <Pagination.Item active>{pageNumber} / {statements.totalPages}</Pagination.Item>
        <Pagination.Next disabled={pageNumber >= statements.totalPages || loading} onClick={() => void loadStatements(pageNumber + 1)} />
      </Pagination>}
    </div>
  </>;
}
