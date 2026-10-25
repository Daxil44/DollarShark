import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Card, Form, Spinner, Table } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { api, apiErrorMessage } from '../api';
import type { DashboardCurrencySummary, DashboardOverview, DashboardRecentTransaction } from '../types';

type Period = { from: string; to: string };
const recentLimit = 8;

function currentMonth(): Period {
  const today = new Date();
  const year = today.getFullYear();
  const month = today.getMonth();
  const from = new Date(year, month, 1);
  const to = new Date(year, month + 1, 0);
  return { from: dateInput(from), to: dateInput(to) };
}

function dateInput(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function displayDate(value: string): string {
  return new Date(`${value}T00:00:00`).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function money(value: number, currency: string): string {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 2 }).format(value);
}

function Comparison({ current, previous, currency, previousPeriod }: {
  current: number; previous: number; currency: string; previousPeriod: string;
}) {
  const difference = current - previous;
  const change = `${difference > 0 ? '+' : ''}${money(difference, currency)}`;
  return <div className="stat-note">Previous period ({previousPeriod}): {money(previous, currency)} <span className="mx-1">·</span>{change} vs previous</div>;
}

function SummaryCards({ summary, previousPeriod }: { summary: DashboardCurrencySummary; previousPeriod: string }) {
  return <section className="mb-4" aria-label={`${summary.currency} dashboard totals`}>
    <div className="d-flex justify-content-between align-items-baseline mb-2">
      <h2 className="section-title mb-0">{summary.currency}</h2>
      <span className="small text-secondary">Compared with {previousPeriod}</span>
    </div>
    <div className="row g-3">
      <div className="col-md-4"><Card className="stat-card h-100"><Card.Body>
        <div className="stat-label">INCOME</div><div className="stat-value text-success">{money(summary.income, summary.currency)}</div>
        <Comparison current={summary.income} previous={summary.previousIncome} currency={summary.currency} previousPeriod={previousPeriod} />
      </Card.Body></Card></div>
      <div className="col-md-4"><Card className="stat-card h-100"><Card.Body>
        <div className="stat-label">EXPENSES</div><div className="stat-value text-danger">{money(summary.expenses, summary.currency)}</div>
        <Comparison current={summary.expenses} previous={summary.previousExpenses} currency={summary.currency} previousPeriod={previousPeriod} />
      </Card.Body></Card></div>
      <div className="col-md-4"><Card className="stat-card h-100"><Card.Body>
        <div className="stat-label">NET CASH FLOW</div><div className={`stat-value ${summary.netCashFlow < 0 ? 'text-danger' : 'text-success'}`}>{money(summary.netCashFlow, summary.currency)}</div>
        <Comparison current={summary.netCashFlow} previous={summary.previousNetCashFlow} currency={summary.currency} previousPeriod={previousPeriod} />
      </Card.Body></Card></div>
    </div>
  </section>;
}

function RecentTransactionRow({ transaction }: { transaction: DashboardRecentTransaction }) {
  return <tr>
    <td className="text-nowrap">{displayDate(transaction.transactionDate)}</td>
    <td><strong>{transaction.description}</strong></td>
    <td>{transaction.accountName}</td>
    <td>{transaction.categoryName ?? <span className="text-secondary">Uncategorized</span>}</td>
    <td className={transaction.amount < 0 ? 'text-danger text-nowrap text-end' : 'text-success text-nowrap text-end'}>
      {money(transaction.amount, transaction.currency)}
    </td>
    <td><Badge bg={transaction.transactionType === 'Transfer' ? 'info' : 'secondary'} text={transaction.transactionType === 'Transfer' ? 'dark' : undefined}>{transaction.transactionType}</Badge></td>
  </tr>;
}

export default function DashboardPage() {
  const initialPeriod = currentMonth();
  const [draftPeriod, setDraftPeriod] = useState(initialPeriod);
  const [period, setPeriod] = useState(initialPeriod);
  const [overview, setOverview] = useState<DashboardOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadOverview = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await api.get<DashboardOverview>('/api/dashboard/overview', {
        params: { from: period.from, to: period.to, recentLimit }
      });
      setOverview(response.data);
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [period.from, period.to]);

  useEffect(() => { void loadOverview(); }, [loadOverview]);

  function applyPeriod(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draftPeriod.from || !draftPeriod.to) {
      setError('Choose both a start date and an end date.');
      return;
    }
    if (draftPeriod.from > draftPeriod.to) {
      setError('The end date must be on or after the start date.');
      return;
    }
    setError('');
    setPeriod({ ...draftPeriod });
  }

  const previousRange = overview
    ? `${displayDate(overview.previousPeriodStart)} – ${displayDate(overview.previousPeriodEnd)}`
    : '';
  const hasTransactions = (overview?.currencies.length ?? 0) > 0 || (overview?.recentTransactions.length ?? 0) > 0;

  return <>
    <div className="page-heading">
      <div><div className="eyebrow">YOUR MONEY, AT A GLANCE</div><h1>Dashboard</h1><p className="text-secondary mb-0">A clear view of your saved financial activity.</p></div>
      <Link to="/transactions" className="btn btn-dark">＋ Add transaction</Link>
    </div>

    {error && <Alert variant="danger" dismissible onClose={() => setError('')}>
      <div className="d-flex justify-content-between align-items-center gap-3"><span>{error}</span><Button variant="outline-danger" size="sm" onClick={() => void loadOverview()}>Retry</Button></div>
    </Alert>}

    <div className="content-card p-3 p-md-4 mb-4">
      <Form onSubmit={applyPeriod}>
        <div className="row g-3 align-items-end">
          <Form.Group className="col-6 col-md-3"><Form.Label>From</Form.Label><Form.Control type="date" required value={draftPeriod.from} onChange={event => setDraftPeriod(current => ({ ...current, from: event.target.value }))} /></Form.Group>
          <Form.Group className="col-6 col-md-3"><Form.Label>To</Form.Label><Form.Control type="date" required value={draftPeriod.to} onChange={event => setDraftPeriod(current => ({ ...current, to: event.target.value }))} /></Form.Group>
          <div className="col-6 col-md-3"><Button type="submit" variant="outline-dark" className="w-100" disabled={loading}>Apply period</Button></div>
          <div className="col-6 col-md-3"><Button type="button" variant="outline-secondary" className="w-100" onClick={() => void loadOverview()} disabled={loading}>Refresh</Button></div>
        </div>
      </Form>
      {overview && <div className="small text-secondary mt-3">Showing {displayDate(overview.periodStart)} – {displayDate(overview.periodEnd)}</div>}
    </div>

    {loading ? <div className="content-card text-center py-5"><Spinner animation="border" role="status"><span className="visually-hidden">Loading dashboard</span></Spinner></div> : overview && <>
      {overview.currencies.map(summary => <SummaryCards key={summary.currency} summary={summary} previousPeriod={previousRange} />)}

      <div className="content-card p-3 p-md-4">
        <div className="d-flex justify-content-between align-items-center gap-3 mb-3">
          <div><h2 className="section-title">Recent transactions</h2><p className="small text-secondary mb-0">Saved transactions in the selected period</p></div>
          <Link to="/transactions" className="btn btn-outline-dark btn-sm">All transactions</Link>
        </div>
        {overview.recentTransactions.length === 0 ? <div className="empty-state">
          <div className="empty-icon">↔</div>
          <h3>{hasTransactions ? 'No transactions in this period' : 'No saved transactions yet'}</h3>
          <p className="text-secondary mb-3">The dashboard uses saved transactions only. CSV previews are not included.</p>
          <Link to="/transactions" className="btn btn-outline-dark">Add a transaction manually</Link>
        </div> : <Table responsive hover className="align-middle mb-0">
          <thead><tr><th>Date</th><th>Description</th><th>Account</th><th>Category</th><th className="text-end">Amount</th><th>Type</th></tr></thead>
          <tbody>{overview.recentTransactions.map(transaction => <RecentTransactionRow key={transaction.id} transaction={transaction} />)}</tbody>
        </Table>}
      </div>
    </>}
  </>;
}
