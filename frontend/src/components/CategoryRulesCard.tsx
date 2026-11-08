import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Form, Modal, Spinner, Table } from 'react-bootstrap';
import { api, apiErrorMessage } from '../api';
import type { ApplyCategoryRulesResult, Category, CategoryRule, CategoryRuleMatchType, StarterRulesResult } from '../types';

type RuleForm = { pattern: string; matchType: CategoryRuleMatchType; categoryId: string; priority: string };
const blankRule = (): RuleForm => ({ pattern: '', matchType: 'Contains', categoryId: '', priority: '100' });
const matchLabels: Record<CategoryRuleMatchType, string> = { Contains: 'contains', StartsWith: 'starts with', Equals: 'is exactly' };

/** Shows how a description is matched, mirroring the API (case-insensitive, repeated spaces ignored). */
function matches(rule: RuleForm, sample: string): boolean {
  const normalize = (value: string) => value.trim().split(/\s+/).join(' ').toLowerCase();
  const text = normalize(sample);
  const pattern = normalize(rule.pattern);
  if (!text || !pattern) return false;
  return rule.matchType === 'Contains' ? text.includes(pattern) : rule.matchType === 'StartsWith' ? text.startsWith(pattern) : text === pattern;
}

export default function CategoryRulesCard({ categories, onApplied }: { categories: Category[]; onApplied: () => void }) {
  const [rules, setRules] = useState<CategoryRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<CategoryRule | null>(null);
  const [form, setForm] = useState<RuleForm>(blankRule());
  const [formError, setFormError] = useState('');
  const [sample, setSample] = useState('');
  const [saving, setSaving] = useState(false);
  const [applying, setApplying] = useState(false);
  const [addingStarter, setAddingStarter] = useState(false);
  const [search, setSearch] = useState('');

  const loadRules = useCallback(async () => {
    setLoading(true);
    try {
      setRules((await api.get<CategoryRule[]>('/api/category-rules')).data);
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadRules(); }, [loadRules]);

  const query = search.trim().toLowerCase();
  const visibleRules = query ? rules.filter(rule => rule.pattern.toLowerCase().includes(query) || rule.categoryName.toLowerCase().includes(query)) : rules;

  function openCreate() {
    setEditing(null);
    setForm(blankRule());
    setSample('');
    setFormError('');
    setShowForm(true);
  }

  function openEdit(rule: CategoryRule) {
    setEditing(rule);
    setForm({ pattern: rule.pattern, matchType: rule.matchType, categoryId: rule.categoryId, priority: String(rule.priority) });
    setSample('');
    setFormError('');
    setShowForm(true);
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setFormError('');
    const body = { pattern: form.pattern, matchType: form.matchType, categoryId: form.categoryId || null, priority: Number(form.priority) || 100 };
    try {
      if (editing) await api.put(`/api/category-rules/${editing.id}`, body);
      else await api.post('/api/category-rules', body);
      setShowForm(false);
      setNotice(editing ? 'Rule updated.' : 'Rule created. New imports will use it — apply it to existing transactions below.');
      await loadRules();
    } catch (saveError) {
      setFormError(apiErrorMessage(saveError));
    } finally {
      setSaving(false);
    }
  }

  async function remove(rule: CategoryRule) {
    if (!window.confirm(`Delete the rule "${rule.pattern}" → ${rule.categoryName}? Transactions it already categorized keep their category.`)) return;
    setError('');
    try {
      await api.delete(`/api/category-rules/${rule.id}`);
      setNotice('Rule deleted.');
      await loadRules();
    } catch (deleteError) {
      setError(apiErrorMessage(deleteError));
    }
  }

  async function addStarterRules() {
    setAddingStarter(true);
    setError('');
    setNotice('');
    try {
      const result = (await api.post<StarterRulesResult>('/api/category-rules/starter')).data;
      const parts = [result.addedCount === 0 ? 'You already have all the common-merchant rules.' : `Added ${result.addedCount} common-merchant rules. Edit or delete any of them below.`];
      if (result.skippedForLimitCount > 0) parts.push(`${result.skippedForLimitCount} were skipped because you reached the rule limit.`);
      setNotice(parts.join(' '));
      await loadRules();
    } catch (starterError) {
      setError(apiErrorMessage(starterError));
    } finally {
      setAddingStarter(false);
    }
  }

  async function applyRules() {
    setApplying(true);
    setError('');
    setNotice('');
    try {
      const result = (await api.post<ApplyCategoryRulesResult>('/api/category-rules/apply')).data;
      setNotice(result.updatedCount === 0
        ? `No uncategorized transactions matched your rules (${result.uncategorizedRemaining} still uncategorized).`
        : `Categorized ${result.updatedCount} transaction${result.updatedCount === 1 ? '' : 's'}. ${result.uncategorizedRemaining} still uncategorized.`);
      onApplied();
    } catch (applyError) {
      setError(apiErrorMessage(applyError));
    } finally {
      setApplying(false);
    }
  }

  return <div className="content-card p-3 p-md-4 mt-4">
    <div className="d-flex flex-wrap justify-content-between align-items-start gap-3 mb-3">
      <div>
        <div className="eyebrow">AUTO-CATEGORIZATION</div>
        <h2 className="section-title mb-1">Rules</h2>
        <p className="small text-secondary mb-0">When a description matches, the category is filled in for you during CSV import. Matching ignores upper/lower case.</p>
      </div>
      <div className="d-flex flex-wrap gap-2">
        {rules.length > 0 && <Button variant="outline-secondary" size="sm" onClick={() => void addStarterRules()} disabled={addingStarter} title="Adds well-known Canadian merchants you don't have a rule for yet">{addingStarter ? 'Adding…' : '＋ Common merchants'}</Button>}
        <Button variant="outline-dark" size="sm" onClick={() => void applyRules()} disabled={applying || rules.length === 0}>{applying ? <><Spinner size="sm" className="me-1" />Applying…</> : 'Apply to uncategorized'}</Button>
        <Button variant="dark" size="sm" onClick={openCreate}>＋ Add rule</Button>
      </div>
    </div>
    {error && <Alert variant="danger" dismissible onClose={() => setError('')}>{error}</Alert>}
    {notice && <Alert variant="success" dismissible onClose={() => setNotice('')}>{notice}</Alert>}
    {loading ? <div className="text-center py-4"><Spinner animation="border" size="sm" aria-label="Loading rules" /></div> :
      rules.length === 0 ? <div className="empty-state">
        <div className="empty-icon">⚡</div>
        <h3>No rules yet</h3>
        <p className="text-secondary mb-3">Start with rules for well-known Canadian stores — Walmart, Superstore, Tim Hortons, Netflix, Rogers, Petro-Canada and more. They become your own rules, so you can edit or delete any of them.</p>
        <div className="d-flex flex-wrap justify-content-center gap-2">
          <Button variant="dark" size="sm" onClick={() => void addStarterRules()} disabled={addingStarter}>{addingStarter ? <><Spinner size="sm" className="me-1" />Adding…</> : '⚡ Add common merchants'}</Button>
          <Button variant="outline-dark" size="sm" onClick={openCreate}>Create my own rule</Button>
        </div>
      </div> : <>
      {rules.length > 8 && <Form.Control size="sm" type="search" className="rule-search mb-3" placeholder={`Search ${rules.length} rules by text or category`} aria-label="Search rules" value={search} onChange={event => setSearch(event.target.value)} />}
      <Table responsive hover className="align-middle mb-0">
        <thead><tr><th>When description…</th><th>Category</th><th title="Lower numbers win when several rules match">Priority</th><th className="text-end">Actions</th></tr></thead>
        <tbody>{visibleRules.length === 0 ? <tr><td colSpan={4} className="text-center text-secondary py-4">No rules match “{search}”.</td></tr> : visibleRules.map(rule => <tr key={rule.id}>
          <td><span className="text-secondary">{matchLabels[rule.matchType]}</span> <code className="rule-pattern">{rule.pattern}</code></td>
          <td><Badge bg="light" text="dark" className="rule-category">{rule.categoryName}</Badge></td>
          <td>{rule.priority}</td>
          <td className="text-end text-nowrap"><Button size="sm" variant="outline-secondary" className="me-2" onClick={() => openEdit(rule)}>Edit</Button><Button size="sm" variant="outline-danger" onClick={() => void remove(rule)}>Delete</Button></td>
        </tr>)}</tbody>
      </Table></>}

    <Modal show={showForm} onHide={() => setShowForm(false)} centered>
      <Form onSubmit={save}>
        <Modal.Header closeButton><Modal.Title>{editing ? 'Edit rule' : 'Add rule'}</Modal.Title></Modal.Header>
        <Modal.Body>
          {formError && <Alert variant="danger">{formError}</Alert>}
          <div className="row g-2 mb-3">
            <Form.Group className="col-5" controlId="rule-match"><Form.Label>When description</Form.Label>
              <Form.Select value={form.matchType} onChange={event => setForm({ ...form, matchType: event.target.value as CategoryRuleMatchType })}>
                <option value="Contains">contains</option><option value="StartsWith">starts with</option><option value="Equals">is exactly</option>
              </Form.Select></Form.Group>
            <Form.Group className="col-7" controlId="rule-pattern"><Form.Label>Text</Form.Label>
              <Form.Control required minLength={2} maxLength={100} placeholder="FRESHMART" value={form.pattern} onChange={event => setForm({ ...form, pattern: event.target.value })} autoFocus /></Form.Group>
          </div>
          <Form.Group className="mb-3" controlId="rule-category"><Form.Label>Set category to</Form.Label>
            <Form.Select required value={form.categoryId} onChange={event => setForm({ ...form, categoryId: event.target.value })}>
              <option value="">Choose a category</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}
            </Form.Select></Form.Group>
          <Form.Group className="mb-3" controlId="rule-priority"><Form.Label>Priority</Form.Label>
            <Form.Control type="number" min={1} max={1000} value={form.priority} onChange={event => setForm({ ...form, priority: event.target.value })} />
            <Form.Text>If several rules match, the lowest number wins (then the longest text).</Form.Text></Form.Group>
          <Form.Group controlId="rule-test"><Form.Label>Try it <span className="text-secondary fw-normal">(optional)</span></Form.Label>
            <Form.Control placeholder="Paste a description from your statement" value={sample} onChange={event => setSample(event.target.value)} />
            {sample.trim() && <div className={`small mt-1 fw-semibold ${matches(form, sample) ? 'text-success' : 'text-secondary'}`}>{matches(form, sample) ? '✓ This rule matches' : '✗ No match'}</div>}
          </Form.Group>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setShowForm(false)}>Cancel</Button><Button variant="dark" type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save rule'}</Button></Modal.Footer>
      </Form>
    </Modal>
  </div>;
}
