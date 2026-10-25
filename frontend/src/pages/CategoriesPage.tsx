import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Button, Form, Modal, Spinner, Table } from 'react-bootstrap';
import { api, apiErrorMessage } from '../api';
import type { Category, CategoryInput } from '../types';

const blankForm = (): CategoryInput => ({ name: '', parentCategoryId: '' });

export default function CategoriesPage() {
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Category | null>(null);
  const [form, setForm] = useState<CategoryInput>(blankForm());

  const loadCategories = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await api.get<Category[]>('/api/categories');
      setCategories(response.data);
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadCategories(); }, [loadCategories]);

  function openCreate() {
    setEditing(null);
    setForm(blankForm());
    setError('');
    setShowForm(true);
  }

  function openEdit(category: Category) {
    setEditing(category);
    setForm({ name: category.name, parentCategoryId: category.parentCategoryId ?? '' });
    setError('');
    setShowForm(true);
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError('');
    const body = { name: form.name, parentCategoryId: form.parentCategoryId || null };
    try {
      if (editing) await api.put('/api/categories/' + editing.id, body);
      else await api.post('/api/categories', body);
      setShowForm(false);
      setNotice(editing ? 'Category updated.' : 'Category created.');
      await loadCategories();
    } catch (saveError) {
      setError(apiErrorMessage(saveError));
    } finally {
      setSaving(false);
    }
  }

  async function remove(category: Category) {
    if (!window.confirm('Delete "' + category.name + '"?')) return;
    setError('');
    setNotice('');
    try {
      await api.delete('/api/categories/' + category.id);
      setNotice('Category deleted.');
      await loadCategories();
    } catch (deleteError) {
      setError(apiErrorMessage(deleteError));
    }
  }

  return <>
    <div className="page-heading">
      <div><div className="eyebrow">ORGANIZE YOUR FINANCES</div><h1>Categories</h1><p className="text-secondary mb-0">Group transactions in a way that makes sense to you.</p></div>
      <Button variant="dark" onClick={openCreate}>＋ Add category</Button>
    </div>
    {error && <Alert variant="danger" dismissible onClose={() => setError('')}>{error}</Alert>}
    {notice && <Alert variant="success" dismissible onClose={() => setNotice('')}>{notice}</Alert>}
    <div className="content-card p-3 p-md-4">
      {loading ? <div className="text-center py-5"><Spinner animation="border" size="sm" aria-label="Loading categories" /></div> :
        categories.length === 0 ? <div className="empty-state"><div className="empty-icon">＋</div><h3>No categories</h3><p className="text-secondary">Add a category or apply the pending seed migration to load defaults.</p></div> :
        <Table responsive hover className="align-middle mb-0">
          <thead><tr><th>Name</th><th>Parent</th><th>Transactions</th><th className="text-end">Actions</th></tr></thead>
          <tbody>{categories.map(category => <tr key={category.id}>
            <td><strong>{category.name}</strong></td><td>{category.parentCategoryName ?? '—'}</td><td>{category.transactionCount}</td>
            <td className="text-end text-nowrap">{category.isSystem ? <span className="badge text-bg-light">Shared default</span> : <><Button size="sm" variant="outline-secondary" className="me-2" onClick={() => openEdit(category)}>Edit</Button><Button size="sm" variant="outline-danger" disabled={category.transactionCount > 0 || category.childCount > 0} title={category.transactionCount > 0 || category.childCount > 0 ? 'Remove transactions or child categories first.' : undefined} onClick={() => void remove(category)}>Delete</Button></>}</td>
          </tr>)}</tbody>
        </Table>}
    </div>
    <Modal show={showForm} onHide={() => setShowForm(false)} centered>
      <Form onSubmit={save}>
        <Modal.Header closeButton><Modal.Title>{editing ? 'Edit category' : 'Add category'}</Modal.Title></Modal.Header>
        <Modal.Body>
          {error && <Alert variant="danger">{error}</Alert>}
          <Form.Group className="mb-3"><Form.Label>Name</Form.Label><Form.Control required maxLength={80} value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} /></Form.Group>
          <Form.Group><Form.Label>Parent category (optional)</Form.Label><Form.Select value={form.parentCategoryId} onChange={event => setForm({ ...form, parentCategoryId: event.target.value })}><option value="">No parent</option>{categories.filter(category => category.id !== editing?.id).map(category => <option key={category.id} value={category.id}>{category.name}</option>)}</Form.Select><Form.Text>Parent assignments are validated by the API to prevent cycles.</Form.Text></Form.Group>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setShowForm(false)}>Cancel</Button><Button variant="dark" type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save category'}</Button></Modal.Footer>
      </Form>
    </Modal>
  </>;
}
