import { useEffect, useState } from 'react';
import { Button } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';

type Progress = { accounts: number; statements: number; transactions: number; rules: number };

const steps = [
  { key: 'accounts', title: 'Add your first account', text: 'Chequing, savings or a credit card, with its opening balance.', to: '/accounts', action: 'Add account' },
  { key: 'statements', title: 'Upload a statement', text: 'Export a CSV from your bank and drop it in. Nothing is imported yet.', to: '/statements', action: 'Upload CSV' },
  { key: 'transactions', title: 'Review & import', text: 'Check the preview, skip duplicates, then confirm the import.', to: '/statements', action: 'Review import' },
  { key: 'rules', title: 'Teach it your categories', text: 'Optional: add rules for common stores (Walmart, Tim Hortons, Netflix…) in one click so future imports sort themselves.', to: '/categories', action: 'Add rules' },
] as const;

function readDismissed(key: string): boolean {
  try { return window.localStorage.getItem(key) === '1'; } catch { return false; }
}

/** Checklist shown to new users until the core steps are done or they hide it. Progress is read from real data. */
export default function FirstRunGuide() {
  const { user } = useAuth();
  const storageKey = `dollarshark.firstRunGuide.hidden.${user?.id ?? 'anonymous'}`;
  const [progress, setProgress] = useState<Progress | null>(null);
  const [hidden, setHidden] = useState(() => readDismissed(storageKey));

  useEffect(() => {
    if (hidden) return;
    let active = true;
    Promise.all([
      api.get<unknown[]>('/api/accounts', { params: { includeInactive: true } }),
      api.get<{ totalCount: number }>('/api/statements', { params: { page: 1, pageSize: 1 } }),
      api.get<{ totalCount: number }>('/api/transactions', { params: { page: 1, pageSize: 1 } }),
      api.get<unknown[]>('/api/category-rules'),
    ]).then(([accounts, statements, transactions, rules]) => {
      if (active) setProgress({ accounts: accounts.data.length, statements: statements.data.totalCount, transactions: transactions.data.totalCount, rules: rules.data.length });
    }).catch(() => { /* the guide is a helper; never block the dashboard if it can't load */ });
    return () => { active = false; };
  }, [hidden]);

  if (hidden || !progress) return null;
  // Someone who added transactions manually has effectively done the import step too.
  const done = {
    accounts: progress.accounts > 0,
    statements: progress.statements > 0 || progress.transactions > 0,
    transactions: progress.transactions > 0,
    rules: progress.rules > 0,
  };
  const coreDone = done.accounts && done.statements && done.transactions;
  if (coreDone && done.rules) return null;
  const completed = Object.values(done).filter(Boolean).length;
  const nextKey = steps.find(step => !done[step.key])?.key;

  function hide() {
    try { window.localStorage.setItem(storageKey, '1'); } catch { /* storage unavailable: hide for this visit only */ }
    setHidden(true);
  }

  return <section className="first-run content-card p-3 p-md-4 mb-4" aria-label="Getting started">
    <div className="d-flex flex-wrap justify-content-between align-items-start gap-2 mb-3">
      <div>
        <div className="eyebrow">{coreDone ? 'ONE MORE TIP' : 'GETTING STARTED'}</div>
        <h2 className="section-title mb-1">{coreDone ? 'Nice — your money is in. One last tip:' : 'Set up DollarShark in a few minutes'}</h2>
        <p className="small text-secondary mb-0">{completed} of {steps.length} done</p>
      </div>
      <Button variant="link" size="sm" className="text-secondary p-0" onClick={hide}>Hide guide</Button>
    </div>
    <div className="first-run-bar" role="progressbar" aria-valuenow={completed} aria-valuemin={0} aria-valuemax={steps.length} aria-label="Setup progress">
      <span style={{ width: `${(completed / steps.length) * 100}%` }} />
    </div>
    <ol className="first-run-steps">
      {steps.map((step, index) => {
        const isDone = done[step.key];
        const isNext = step.key === nextKey;
        return <li key={step.key} className={isDone ? 'done' : isNext ? 'next' : ''}>
          <span className="first-run-check" aria-hidden="true">{isDone ? '✓' : index + 1}</span>
          <div className="first-run-text"><strong>{step.title}</strong><span>{step.text}</span></div>
          {isDone ? <span className="first-run-status">Done</span> :
            <Link to={step.to} className={`btn btn-sm ${isNext ? 'btn-dark' : 'btn-outline-secondary'}`}>{step.action}</Link>}
        </li>;
      })}
    </ol>
  </section>;
}
