import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Card, Form, Spinner, Table } from 'react-bootstrap';
import { Link, useNavigate } from 'react-router-dom';
import {
  Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer,
  Tooltip, XAxis, YAxis,
} from 'recharts';
import { api, apiErrorMessage } from '../api';
import type { Account, AccountBalanceHistory, Category, DashboardBalanceTotals, DashboardCurrencySummary, DashboardOverview, DashboardRecentTransaction, TransactionPage } from '../types';

type Period = { from: string; to: string };
type Preset = 'thisMonth' | 'lastMonth' | 'last3Months' | 'thisYear' | 'allTime' | 'custom';
const recentLimit = 8;
const chartColors = ['#17845f', '#3176a5', '#e2a23b', '#7858a6', '#d26c55', '#5c9c99', '#80914c', '#8d6e63'];

function dateInput(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function periodFor(preset: Exclude<Preset, 'custom'>): Period {
  const now = new Date();
  const year = now.getFullYear();
  const month = now.getMonth();
  const today = dateInput(now);
  if (preset === 'allTime') return { from: '1900-01-01', to: today };
  if (preset === 'thisYear') return { from: dateInput(new Date(year, 0, 1)), to: today };
  if (preset === 'last3Months') return { from: dateInput(new Date(year, month - 2, 1)), to: today };
  if (preset === 'lastMonth') return { from: dateInput(new Date(year, month - 1, 1)), to: dateInput(new Date(year, month, 0)) };
  return { from: dateInput(new Date(year, month, 1)), to: today };
}

function displayDate(value: string): string {
  return new Date(`${value}T00:00:00`).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function money(value: number, currency: string): string {
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 2 }).format(value);
  } catch {
    return `${currency} ${value.toFixed(2)}`;
  }
}

function monthLabel(value: string): string {
  const [year, month] = value.split('-').map(Number);
  return new Date(year, month - 1, 1).toLocaleDateString(undefined, { month: 'short', year: '2-digit' });
}

function Comparison({ current, previous, currency, previousPeriod }: {
  current: number; previous: number; currency: string; previousPeriod: string;
}) {
  const difference = current - previous;
  return <div className="stat-note">Previous: {money(previous, currency)} <span className="mx-1">·</span>
    <span className={difference < 0 ? 'text-danger' : 'text-success'}>{difference > 0 ? '+' : ''}{money(difference, currency)}</span>
    <span className="ms-1">({previousPeriod})</span></div>;
}

function SummaryCards({ summary, previousPeriod, balances }: { summary: DashboardCurrencySummary; previousPeriod: string; balances?: DashboardBalanceTotals }) {
  const cashValue = !balances || balances.cashAccountCount === 0 ? 'No accounts' : balances.cashBalance === null ? 'Incomplete' : money(balances.cashBalance, summary.currency);
  const cashNote = !balances || balances.cashAccountCount === 0 ? 'No chequing or savings accounts.' : `${balances.cashAccountsWithBalances} of ${balances.cashAccountCount} accounts have known balances.`;
  const cardValue = !balances || balances.creditCardCount === 0 ? 'No accounts' : balances.creditCardLiabilities === null ? 'Incomplete' : money(balances.creditCardLiabilities, summary.currency);
  const cardNote = !balances || balances.creditCardCount === 0 ? 'No credit card accounts.' : `${balances.creditCardsWithBalances} of ${balances.creditCardCount} accounts have known liabilities.`;
  const investmentValue = !balances || balances.investmentAccountCount === 0 ? 'No accounts' : balances.recordedInvestmentBalances === null ? 'Not available' : money(balances.recordedInvestmentBalances, summary.currency);
  const investmentNote = !balances || balances.investmentAccountCount === 0 ? 'No investment accounts.' : `Recorded for ${balances.investmentsWithBalances} of ${balances.investmentAccountCount} investment accounts.`;
  const cards = [
    { label: 'TOTAL INCOME', value: money(summary.income, summary.currency), note: <><span>Selected period activity; refunds follow existing reporting.</span><Comparison current={summary.income} previous={summary.previousIncome} currency={summary.currency} previousPeriod={previousPeriod} /></>, color: 'text-success' },
    { label: 'TOTAL EXPENSES', value: money(summary.expenses, summary.currency), note: <><span>Selected period activity; transfers excluded.</span><Comparison current={summary.expenses} previous={summary.previousExpenses} currency={summary.currency} previousPeriod={previousPeriod} /></>, color: 'text-danger' },
    { label: 'NET CASH FLOW', value: money(summary.netCashFlow, summary.currency), note: <>Income minus expenses for the selected period; this is not an account balance.<Comparison current={summary.netCashFlow} previous={summary.previousNetCashFlow} currency={summary.currency} previousPeriod={previousPeriod} /></>, color: summary.netCashFlow < 0 ? 'text-danger' : 'text-success' },
    { label: 'CASH BALANCE', value: cashValue, note: cashNote, color: balances?.cashBalance !== null && balances?.cashBalance !== undefined && balances.cashBalance < 0 ? 'text-danger' : 'text-success' },
    { label: 'CREDIT CARD LIABILITIES', value: cardValue, note: cardNote, color: 'text-danger' },
    { label: 'RECORDED INVESTMENTS', value: investmentValue, note: investmentNote, color: 'text-secondary' },
  ];
  return <section className="mb-4" aria-label={`${summary.currency} dashboard totals`}>
    <div className="d-flex justify-content-between align-items-baseline mb-2">
      <h2 className="section-title mb-0">{summary.currency}</h2><span className="small text-secondary">Compared with {previousPeriod}</span>
    </div>
    <div className="row g-3">{cards.map(card => <div className="col-12 col-sm-6 col-xl-4" key={card.label}>
      <Card className="stat-card dashboard-summary-card h-100"><Card.Body><div className="stat-label">{card.label}</div>
        <div className={`stat-value dashboard-stat-value ${card.color}`}>{card.value}</div><div className="stat-note">{card.note}</div>
      </Card.Body></Card>
    </div>)}</div>
  </section>;
}

function RecentTransactionRow({ transaction }: { transaction: DashboardRecentTransaction }) {
  return <tr>
    <td className="text-nowrap">{displayDate(transaction.transactionDate)}</td>
    <td><strong>{transaction.description}</strong></td><td>{transaction.accountName}</td>
    <td>{transaction.categoryName ?? <span className="text-secondary">Uncategorized</span>}</td>
    <td className={transaction.amount < 0 ? 'text-danger text-nowrap text-end' : 'text-success text-nowrap text-end'}>{money(transaction.amount, transaction.currency)}</td>
    <td><Badge bg={transaction.transactionType === 'Transfer' ? 'info' : transaction.transactionType === 'Expense' ? 'danger' : transaction.transactionType === 'Income' ? 'success' : 'secondary'} text={transaction.transactionType === 'Transfer' ? 'dark' : undefined}>{transaction.transactionType}</Badge></td>
  </tr>;
}

export default function DashboardPage() {
  const navigate = useNavigate();
  const initialPeriod = periodFor('thisMonth');
  const [preset, setPreset] = useState<Preset>('thisMonth');
  const [draftPeriod, setDraftPeriod] = useState(initialPeriod);
  const [period, setPeriod] = useState(initialPeriod);
  const [overview, setOverview] = useState<DashboardOverview | null>(null);
  const [currency, setCurrency] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedAccountId, setSelectedAccountId] = useState('');
  const [accountTransactions, setAccountTransactions] = useState<TransactionPage | null>(null);
  const [balanceHistory, setBalanceHistory] = useState<AccountBalanceHistory | null>(null);
  const [balanceInterval, setBalanceInterval] = useState<'Daily' | 'Weekly' | 'Monthly'>('Daily');
  const [balanceLoading, setBalanceLoading] = useState(false);
  const [balanceError, setBalanceError] = useState('');
  const [transactionsLoading, setTransactionsLoading] = useState(false);
  const [transactionError, setTransactionError] = useState('');
  const [transactionFilters, setTransactionFilters] = useState({ search: '', from: '', to: '', transactionType: '', categoryId: '', page: 1 });

  useEffect(() => {
    void Promise.all([api.get<Account[]>('/api/accounts'), api.get<Category[]>('/api/categories')])
      .then(([accountResponse, categoryResponse]) => {
        setAccounts(accountResponse.data.filter(account => account.isActive));
        setCategories(categoryResponse.data);
      })
      .catch(loadError => setError(apiErrorMessage(loadError)));
  }, []);

  const loadOverview = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const response = await api.get<DashboardOverview>('/api/dashboard/overview', { params: { from: period.from, to: period.to, recentLimit, ...(selectedAccountId ? { accountId: selectedAccountId } : {}) } });
      if (!Array.isArray(response.data.accounts) || !Array.isArray(response.data.balanceTotals) ||
          !Array.isArray(response.data.monthlyCashFlow) || !Array.isArray(response.data.accountBalanceTrends)) {
        setOverview(null);
        setError('The API is running an older build without consolidated dashboards. Restart the DollarShark API, then refresh this page.');
        return;
      }
      if (selectedAccountId && response.data.accountId !== selectedAccountId) {
        setOverview(null);
        setError('The API is running an older build without account dashboards. Restart the DollarShark API, then refresh this page.');
        return;
      }
      setOverview(response.data);
      setCurrency(current => response.data.currencies.some(item => item.currency === current) ? current : response.data.currencies[0]?.currency ?? '');
    } catch (loadError) {
      setError(apiErrorMessage(loadError));
    } finally {
      setLoading(false);
    }
  }, [period.from, period.to, selectedAccountId]);

  useEffect(() => { void loadOverview(); }, [loadOverview]);

  useEffect(() => {
    if (!selectedAccountId) { setAccountTransactions(null); return; }
    let cancelled = false;
    setTransactionsLoading(true); setTransactionError('');
    const params = new URLSearchParams({
      accountId: selectedAccountId, from: transactionFilters.from || period.from, to: transactionFilters.to || period.to,
      page: String(transactionFilters.page), pageSize: '10', sortBy: 'date', sortDirection: 'desc',
    });
    if (transactionFilters.search.trim()) params.set('search', transactionFilters.search.trim());
    if (transactionFilters.transactionType) params.set('transactionType', transactionFilters.transactionType);
    if (transactionFilters.categoryId) params.set('categoryId', transactionFilters.categoryId);
    void api.get<TransactionPage>('/api/transactions?' + params.toString())
      .then(response => { if (!cancelled) setAccountTransactions(response.data); })
      .catch(loadError => { if (!cancelled) setTransactionError(apiErrorMessage(loadError)); })
      .finally(() => { if (!cancelled) setTransactionsLoading(false); });
    return () => { cancelled = true; };
  }, [selectedAccountId, period.from, period.to, transactionFilters]);

  useEffect(() => {
    if (!selectedAccountId) { setBalanceHistory(null); setBalanceError(''); return; }
    let cancelled = false;
    setBalanceLoading(true); setBalanceError('');
    void api.get<AccountBalanceHistory>(`/api/dashboard/accounts/${selectedAccountId}/balance-history`, {
      params: { from: period.from, to: period.to, interval: balanceInterval },
    }).then(response => { if (!cancelled) setBalanceHistory(response.data); })
      .catch(loadError => { if (!cancelled) { setBalanceHistory(null); setBalanceError(apiErrorMessage(loadError)); } })
      .finally(() => { if (!cancelled) setBalanceLoading(false); });
    return () => { cancelled = true; };
  }, [selectedAccountId, period.from, period.to, balanceInterval]);

  function choosePreset(value: Preset) {
    setPreset(value);
    if (value === 'allTime' || value === 'thisYear') setBalanceInterval('Monthly');
    else if (value !== 'custom') setBalanceInterval('Daily');
    if (value !== 'custom') {
      const next = periodFor(value);
      setDraftPeriod(next);
      setPeriod(next);
    }
  }

  function applyPeriod(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draftPeriod.from || !draftPeriod.to) { setError('Choose both a start date and an end date.'); return; }
    if (draftPeriod.from > draftPeriod.to) { setError('The end date must be on or after the start date.'); return; }
    setError(''); setPeriod({ ...draftPeriod });
  }

  function changeTransactionFilter(key: keyof typeof transactionFilters, value: string) {
    setTransactionFilters(current => ({ ...current, [key]: value, page: key === 'page' ? Number(value) : 1 }));
  }

  const previousRange = overview ? `${displayDate(overview.previousPeriodStart)} – ${displayDate(overview.previousPeriodEnd)}` : '';
  const categoryData = useMemo(() => overview?.categoryExpenses.filter(item => item.currency === currency) ?? [], [overview, currency]);
  const monthlyData = useMemo(() => overview?.monthlyTotals.filter(item => item.currency === currency).map(item => ({ ...item, label: monthLabel(item.month) })) ?? [], [overview, currency]);
  const monthlyCashFlowData = useMemo(() => overview?.monthlyCashFlow.filter(item => item.currency === currency).map(item => ({ ...item, label: monthLabel(item.month) })) ?? [], [overview, currency]);
  const accountBalanceTrends = useMemo(() => overview?.accountBalanceTrends.filter(item => item.currency === currency) ?? [], [overview, currency]);
  const hasTransactions = (overview?.currencies.some(item => item.transactionCount > 0) ?? false) || (overview?.recentTransactions.length ?? 0) > 0;
  const selectedAccount = accounts.find(account => account.id === selectedAccountId) ?? null;
  const accountType = selectedAccount?.accountType ?? '';
  const accountSummary = overview?.currencies.find(item => item.currency === selectedAccount?.currency);
  const isInvestment = accountType === 'Investment';
  const isCreditCard = accountType === 'Credit Card';

  return <>
    <div className="page-heading">
      <div><div className="eyebrow">YOUR MONEY, AT A GLANCE</div><h1>Dashboard</h1><p className="text-secondary mb-0">A clear view of your saved financial activity.</p></div>
      <Link to="/transactions" className="btn btn-dark">＋ Add transaction</Link>
    </div>

    {error && <Alert variant="danger" dismissible onClose={() => setError('')}><div className="d-flex justify-content-between align-items-center gap-3"><span>{error}</span><Button variant="outline-danger" size="sm" onClick={() => void loadOverview()}>Retry</Button></div></Alert>}

    <div className="content-card p-3 p-md-4 mb-4">
      <Form onSubmit={applyPeriod}>
        <div className="row g-3 align-items-end">
          <Form.Group className="col-12 col-md-5 col-xl-4"><Form.Label htmlFor="dashboard-account">Dashboard view</Form.Label><Form.Select id="dashboard-account" value={selectedAccountId} onChange={event => { setSelectedAccountId(event.target.value); setTransactionFilters(current => ({ ...current, page: 1 })); }}>
            <option value="">Overall dashboard</option>{accounts.map(account => <option value={account.id} key={account.id}>{account.name} · {account.accountType}</option>)}
          </Form.Select></Form.Group>
          <Form.Group className="col-12 col-md-4 col-xl-3"><Form.Label>Date range</Form.Label><Form.Select value={preset} onChange={event => choosePreset(event.target.value as Preset)}>
            <option value="thisMonth">This month</option><option value="lastMonth">Last month</option><option value="last3Months">Last 3 months</option><option value="thisYear">This year</option><option value="allTime">All time</option><option value="custom">Custom range</option>
          </Form.Select></Form.Group>
          {preset === 'custom' && <>
            <Form.Group className="col-6 col-md-3 col-xl-2"><Form.Label>From</Form.Label><Form.Control type="date" required value={draftPeriod.from} onChange={event => setDraftPeriod(current => ({ ...current, from: event.target.value }))} /></Form.Group>
            <Form.Group className="col-6 col-md-3 col-xl-2"><Form.Label>To</Form.Label><Form.Control type="date" required value={draftPeriod.to} onChange={event => setDraftPeriod(current => ({ ...current, to: event.target.value }))} /></Form.Group>
            <div className="col-6 col-md-3 col-xl-2"><Button type="submit" variant="outline-dark" className="w-100" disabled={loading}>Apply</Button></div>
          </>}
          <div className="col-6 col-md-3 col-xl-2"><Button type="button" variant="outline-secondary" className="w-100" onClick={() => void loadOverview()} disabled={loading}>Refresh</Button></div>
          {overview && <div className="col-12 small text-secondary">Showing {displayDate(overview.periodStart)} – {displayDate(overview.periodEnd)}</div>}
        </div>
      </Form>
    </div>

    {loading ? <div className="content-card text-center py-5"><Spinner animation="border" role="status"><span className="visually-hidden">Loading dashboard</span></Spinner></div> : overview && <>
      {selectedAccount && accountSummary ? <section className="mb-4" aria-label={`${selectedAccount.name} account summary`}>
        <div className="d-flex justify-content-between align-items-start gap-3 mb-3"><div><div className="eyebrow">ACCOUNT DASHBOARD</div><h2 className="section-title mb-1">{selectedAccount.name}</h2><div className="text-secondary">{selectedAccount.institution} · {selectedAccount.accountType} · {selectedAccount.currency}</div></div><Badge bg={selectedAccount.classification === 'Liability' ? 'warning' : 'success'} text={selectedAccount.classification === 'Liability' ? 'dark' : undefined}>{selectedAccount.classification}</Badge></div>
        <div className="row g-3">{[
          { label: isCreditCard ? 'OPENING OUTSTANDING BALANCE' : isInvestment ? 'OPENING ACCOUNT VALUE' : 'OPENING BALANCE', value: selectedAccount.openingBalance === null ? 'Not configured' : money(selectedAccount.openingBalance, selectedAccount.currency), note: selectedAccount.openingBalanceDate ? `As of ${displayDate(selectedAccount.openingBalanceDate)}` : 'Set an opening balance to establish a reliable balance.', tone: 'text-secondary' },
          { label: isCreditCard ? 'CURRENT OUTSTANDING BALANCE' : isInvestment ? 'CURRENT ACCOUNT VALUE' : 'CURRENT CASH BALANCE', value: selectedAccount.calculatedBalance === null ? 'Not available' : money(selectedAccount.calculatedBalance, selectedAccount.currency), note: selectedAccount.calculatedBalance === null ? 'Configure an opening balance and date to calculate this.' : 'Opening balance plus recorded account movements.', tone: selectedAccount.calculatedBalance !== null && selectedAccount.calculatedBalance < 0 ? 'text-danger' : 'text-success' },
          { label: isCreditCard ? 'CREDITS & PAYMENTS' : isInvestment ? 'MONEY RECEIVED' : 'MONEY RECEIVED', value: money(accountSummary.moneyReceived, accountSummary.currency), note: 'Positive movements in this account during the selected period.', tone: 'text-success' },
          { label: isCreditCard ? 'PURCHASES & CHARGES' : isInvestment ? 'MONEY SPENT' : 'MONEY SPENT', value: money(accountSummary.moneySpent, accountSummary.currency), note: 'Negative movements in this account during the selected period.', tone: 'text-danger' },
          { label: 'TRANSACTIONS', value: String(accountSummary.transactionCount), note: 'Including transfers in this account and date range.', tone: 'text-body' },
        ].map(card => <div className="col-12 col-sm-6 col-xl" key={card.label}><Card className="stat-card h-100"><Card.Body><div className="stat-label">{card.label}</div><div className={`stat-value dashboard-stat-value ${card.tone}`}>{card.value}</div><div className="stat-note">{card.note}</div></Card.Body></Card></div>)}</div>
      </section> : overview.currencies.map(summary => <SummaryCards key={summary.currency} summary={summary} previousPeriod={previousRange} balances={overview.balanceTotals.find(item => item.currency === summary.currency)} />)}
      {!overview.currencies.length && !selectedAccount && <div className="empty-state mb-4"><div className="empty-icon">$</div><h3>No financial activity yet</h3><p className="text-secondary mb-0">Add an account and opening balance to start tracking account balances.</p></div>}

      {!selectedAccount && <div className="content-card p-3 p-md-4 mb-4">
        <div className="mb-3"><h2 className="section-title">Accounts by type</h2><p className="small text-secondary mb-0">Active accounts and current balances. Accounts without an opening balance are shown as unavailable.</p></div>
        {overview.accounts.length === 0 ? <div className="empty-state"><div className="empty-icon">＋</div><h3>No active accounts</h3><p className="text-secondary mb-0">Add a financial account to see it in this overview.</p></div> : <div className="row g-3">{[
          { type: 'Chequing', label: 'Chequing' }, { type: 'Savings', label: 'Savings' },
          { type: 'Credit Card', label: 'Credit Cards' }, { type: 'Investment', label: 'Investment' },
        ].map(group => {
          const groupAccounts = overview.accounts.filter(account => account.accountType === group.type);
          return <div className="col-12 col-md-6 col-xl-3" key={group.type}><Card className="stat-card h-100"><Card.Body><h3 className="section-title h6">{group.label}</h3>{groupAccounts.length === 0 ? <p className="small text-secondary mb-0">No active accounts.</p> : <div className="d-flex flex-column gap-3">{groupAccounts.map(account => <div className="border-top pt-3" key={account.id}><div className="d-flex justify-content-between align-items-start gap-2"><div><strong>{account.name}</strong><div className="small text-secondary">{account.institution} · {account.currency}</div></div><Badge bg={account.classification === 'Liability' ? 'warning' : 'success'} text={account.classification === 'Liability' ? 'dark' : undefined}>{account.classification}</Badge></div><div className="mt-2"><div className="small text-secondary">{account.accountType === 'Credit Card' ? 'Outstanding liability' : account.accountType === 'Investment' ? 'Recorded account value' : 'Current cash balance'}</div><strong>{account.calculatedBalance === null ? 'Not available' : money(account.calculatedBalance, account.currency)}</strong>{account.calculatedBalance === null && <div className="small text-secondary">Opening balance not configured.</div>}</div><Button size="sm" variant="outline-dark" className="mt-2" onClick={() => setSelectedAccountId(account.id)}>View account dashboard</Button></div>)}</div>}</Card.Body></Card></div>;
        })}</div>}
      </div>}

      {selectedAccount && <div className="content-card p-3 p-md-4 mb-4">
        <div className="d-flex flex-wrap justify-content-between align-items-start gap-3 mb-3"><div><h2 className="section-title">{isCreditCard ? 'Outstanding liability history' : isInvestment ? 'Recorded account balance history' : 'Cash balance history'}</h2><p className="small text-secondary mb-0">Calculated from the opening balance and account-side transactions in {selectedAccount.currency}.</p></div><Form.Group className="d-flex align-items-center gap-2"><Form.Label htmlFor="balance-history-interval" className="mb-0 small text-secondary">View</Form.Label><Form.Select id="balance-history-interval" size="sm" value={balanceInterval} onChange={event => setBalanceInterval(event.target.value as 'Daily' | 'Weekly' | 'Monthly')}><option value="Daily">Daily</option><option value="Weekly">Weekly</option><option value="Monthly">Monthly</option></Form.Select></Form.Group></div>
        {balanceError && <Alert variant="danger">{balanceError}</Alert>}
        {balanceLoading ? <div className="text-center py-4"><Spinner animation="border" role="status"><span className="visually-hidden">Loading balance history</span></Spinner></div> : balanceHistory && !balanceHistory.isAvailable ? <div className="chart-empty">{balanceHistory.message}</div> : balanceHistory && balanceHistory.points.length === 0 ? <div className="chart-empty">{balanceHistory.message ?? 'No balance history is available for the selected date range.'}</div> : balanceHistory && <>
          {balanceHistory.message && <Alert variant="info" className="py-2">{balanceHistory.message}</Alert>}
          <div className="dashboard-bar"><ResponsiveContainer width="100%" height="100%"><LineChart data={balanceHistory.points.map(point => ({ ...point, label: displayDate(point.date) }))} margin={{ top: 8, right: 12, left: 8, bottom: 4 }}><CartesianGrid stroke="#edf1ef" vertical={false} /><XAxis dataKey="label" tickLine={false} axisLine={false} minTickGap={24} /><YAxis tickLine={false} axisLine={false} tickFormatter={value => new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(Number(value))} /><Tooltip formatter={value => money(Number(value), balanceHistory.currency)} /><Line type="monotone" dataKey="balance" name={balanceHistory.classification === 'Liability' ? 'Outstanding balance' : 'Account balance'} stroke={balanceHistory.classification === 'Liability' ? '#d87565' : '#17845f'} strokeWidth={2.5} dot={balanceHistory.points.length < 40} activeDot={{ r: 5 }} /></LineChart></ResponsiveContainer></div>
          <div className="small text-secondary mt-2">{displayDate(balanceHistory.periodStart)} – {displayDate(balanceHistory.periodEnd)} · {balanceHistory.interval.toLowerCase()} balance</div>
        </>}
      </div>}

      {overview.currencies.length > 1 && <div className="d-flex align-items-center gap-2 mb-3"><Form.Label className="mb-0 small text-secondary" htmlFor="dashboard-currency">Charts currency</Form.Label><Form.Select id="dashboard-currency" size="sm" className="dashboard-currency-select" value={currency} onChange={event => setCurrency(event.target.value)}>{overview.currencies.map(item => <option key={item.currency} value={item.currency}>{item.currency}</option>)}</Form.Select></div>}

      <div className="row g-3 mb-4">
        <div className={`col-12 ${isInvestment ? '' : 'col-xl-5'}`}><Card className="content-card h-100"><Card.Body>
          <div className="mb-3"><h2 className="section-title">Spending by category</h2><p className="small text-secondary mb-0">Expenses in {currency || 'your selected period'}; transfers excluded</p></div>
          {categoryData.length === 0 ? <div className="chart-empty">No expense data for this currency and date range.</div> : <>
            <div className="dashboard-donut"><ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={categoryData} dataKey="amount" nameKey="categoryName" cx="50%" cy="50%" innerRadius="58%" outerRadius="82%" paddingAngle={2}>
              {categoryData.map((item, index) => <Cell key={`${item.currency}-${item.categoryName}`} fill={chartColors[index % chartColors.length]} />)}
            </Pie><Tooltip formatter={(value) => money(Number(value), currency)} /><Legend /></PieChart></ResponsiveContainer></div>
            <div className="category-breakdown">{categoryData.map((item, index) => {
              const total = categoryData.reduce((sum, entry) => sum + entry.amount, 0);
              const percentage = total ? item.amount / total * 100 : 0;
              return <div className="category-breakdown-row" key={`${item.currency}-${item.categoryName}`}><span className="category-swatch" style={{ backgroundColor: chartColors[index % chartColors.length] }} /><span className="text-truncate">{item.categoryName}</span><span className="text-secondary">{percentage.toFixed(0)}%</span><strong>{money(item.amount, item.currency)}</strong></div>;
            })}</div>
          </>}
        </Card.Body></Card></div>
        {!isInvestment && <div className="col-12 col-xl-7"><Card className="content-card h-100"><Card.Body>
          <div className="mb-3"><h2 className="section-title">{selectedAccount && isCreditCard ? 'Purchases and credits' : 'Income vs expenses'}</h2><p className="small text-secondary mb-0">Monthly totals in {currency || 'your selected period'}; transfers excluded</p></div>
          {monthlyData.length === 0 ? <div className="chart-empty">No monthly activity for this currency and date range.</div> : <div className="dashboard-bar"><ResponsiveContainer width="100%" height="100%"><BarChart data={monthlyData} margin={{ top: 8, right: 8, left: 0, bottom: 4 }}>
            <CartesianGrid stroke="#edf1ef" vertical={false} /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis tickLine={false} axisLine={false} tickFormatter={value => new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(Number(value))} />
            <Tooltip formatter={(value, name) => [money(Number(value), currency), name]} /><Legend /><Bar dataKey="income" name={selectedAccount && isCreditCard ? 'Credits and refunds' : 'Income'} fill="#17845f" radius={[4, 4, 0, 0]} /><Bar dataKey="expenses" name={selectedAccount && isCreditCard ? 'Purchases' : 'Expenses'} fill="#d87565" radius={[4, 4, 0, 0]} />
          </BarChart></ResponsiveContainer></div>}
        </Card.Body></Card></div>}
      </div>

      {!selectedAccount && <div className="row g-3 mb-4">
        <div className="col-12 col-xl-6"><Card className="content-card h-100"><Card.Body><div className="mb-3"><h2 className="section-title">Monthly cash flow</h2><p className="small text-secondary mb-0">Income minus expenses, and remaining after outgoing transfers. Neither is an account balance.</p></div>
          {monthlyCashFlowData.length === 0 ? <div className="chart-empty">No monthly cash flow for this currency and date range.</div> : <div className="dashboard-bar"><ResponsiveContainer width="100%" height="100%"><BarChart data={monthlyCashFlowData}><CartesianGrid stroke="#edf1ef" vertical={false} /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis tickLine={false} axisLine={false} tickFormatter={value => new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(Number(value))} /><Tooltip formatter={(value, name) => [money(Number(value), currency), name]} /><Legend /><Bar dataKey="netCashFlow" name="Income minus expenses" fill="#17845f" radius={[4, 4, 0, 0]} /><Bar dataKey="remainingAfterExpensesAndTransfers" name="After outgoing transfers" fill="#3176a5" radius={[4, 4, 0, 0]} /></BarChart></ResponsiveContainer></div>}
        </Card.Body></Card></div>
        <div className="col-12 col-xl-6"><Card className="content-card h-100"><Card.Body><div className="mb-3"><h2 className="section-title">Monthly income distribution</h2><p className="small text-secondary mb-0">Expenses and outgoing transfers are shown once, at the sending account. Remaining cash flow is not savings-account balance.</p></div>
          {monthlyCashFlowData.length === 0 ? <div className="chart-empty">No monthly distribution for this currency and date range.</div> : <div className="dashboard-bar"><ResponsiveContainer width="100%" height="100%"><BarChart data={monthlyCashFlowData}><CartesianGrid stroke="#edf1ef" vertical={false} /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis tickLine={false} axisLine={false} tickFormatter={value => new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(Number(value))} /><Tooltip formatter={(value, name) => [money(Number(value), currency), name]} /><Legend /><Bar dataKey="expenses" name="Ordinary expenses" stackId="distribution" fill="#d87565" /><Bar dataKey="transfersToOtherAccounts" name="Other account transfers" stackId="distribution" fill="#3176a5" /><Bar dataKey="investmentContributions" name="Investment contributions" stackId="distribution" fill="#7858a6" /><Bar dataKey="remainingAfterExpensesAndTransfers" name="Remaining after expenses/transfers" stackId="distribution" fill="#17845f" /></BarChart></ResponsiveContainer></div>}
        </Card.Body></Card></div>
      </div>}

      {!selectedAccount && <div className="content-card p-3 p-md-4 mb-4"><div className="mb-3"><h2 className="section-title">Account balance trends</h2><p className="small text-secondary mb-0">Each account and currency is charted separately. Credit card lines show outstanding liabilities.</p></div>
        {accountBalanceTrends.length === 0 ? <div className="chart-empty">No balance trends are available. Configure opening balances for active accounts to track them.</div> : <div className="row g-3">{accountBalanceTrends.map(trend => <div className="col-12 col-xl-6" key={trend.accountId}><Card className="stat-card h-100"><Card.Body><div className="d-flex justify-content-between align-items-start gap-2 mb-2"><div><h3 className="section-title h6 mb-1">{trend.accountName}</h3><div className="small text-secondary">{trend.accountType} · {trend.currency}</div></div><Badge bg={trend.classification === 'Liability' ? 'warning' : 'success'} text={trend.classification === 'Liability' ? 'dark' : undefined}>{trend.classification === 'Liability' ? 'Liability' : 'Asset'}</Badge></div><div className="dashboard-bar"><ResponsiveContainer width="100%" height="100%"><LineChart data={trend.points.map(point => ({ ...point, label: displayDate(point.date) }))}><CartesianGrid stroke="#edf1ef" vertical={false} /><XAxis dataKey="label" tickLine={false} axisLine={false} minTickGap={24} /><YAxis tickLine={false} axisLine={false} tickFormatter={value => new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(Number(value))} /><Tooltip formatter={value => money(Number(value), trend.currency)} /><Line type="monotone" dataKey="balance" name={trend.classification === 'Liability' ? 'Outstanding liability' : 'Account balance'} stroke={trend.classification === 'Liability' ? '#d87565' : '#17845f'} strokeWidth={2.5} dot={trend.points.length < 24} /></LineChart></ResponsiveContainer></div><Button size="sm" variant="link" className="px-0 mt-2" onClick={() => setSelectedAccountId(trend.accountId)}>Open account dashboard</Button></Card.Body></Card></div>)}</div>}
      </div>}

      {selectedAccount ? <div className="content-card p-3 p-md-4 mb-4">
        <div className="mb-3"><h2 className="section-title">Account activity</h2><p className="small text-secondary mb-0">Transactions for {selectedAccount.name}, including linked transfers.</p></div>
        {(overview.monthlyActivity ?? []).filter(item => item.currency === selectedAccount.currency).length > 0 && <div className="dashboard-bar mb-4"><ResponsiveContainer width="100%" height="100%"><BarChart data={(overview.monthlyActivity ?? []).filter(item => item.currency === selectedAccount.currency).map(item => ({ ...item, label: monthLabel(item.month) }))}><CartesianGrid stroke="#edf1ef" vertical={false} /><XAxis dataKey="label" tickLine={false} axisLine={false} /><YAxis allowDecimals={false} tickLine={false} axisLine={false} /><Tooltip /><Legend /><Bar dataKey="incomeCount" name="Income" stackId="a" fill="#17845f" /><Bar dataKey="expenseCount" name="Expense" stackId="a" fill="#d87565" /><Bar dataKey="refundCount" name="Refund" stackId="a" fill="#3176a5" /><Bar dataKey="transferCount" name="Transfer" stackId="a" fill="#e2a23b" /></BarChart></ResponsiveContainer></div>}
        <div className="row g-2 mb-3 align-items-end">
          <Form.Group className="col-12 col-md-3"><Form.Label>Search</Form.Label><Form.Control value={transactionFilters.search} onChange={event => changeTransactionFilter('search', event.target.value)} placeholder="Description" /></Form.Group>
          <Form.Group className="col-6 col-md-2"><Form.Label>From</Form.Label><Form.Control type="date" value={transactionFilters.from} onChange={event => changeTransactionFilter('from', event.target.value)} /></Form.Group>
          <Form.Group className="col-6 col-md-2"><Form.Label>To</Form.Label><Form.Control type="date" value={transactionFilters.to} onChange={event => changeTransactionFilter('to', event.target.value)} /></Form.Group>
          <Form.Group className="col-6 col-md-2"><Form.Label>Type</Form.Label><Form.Select value={transactionFilters.transactionType} onChange={event => changeTransactionFilter('transactionType', event.target.value)}><option value="">All types</option><option>Income</option><option>Expense</option><option>Refund</option><option>Transfer</option></Form.Select></Form.Group>
          <Form.Group className="col-6 col-md-3"><Form.Label>Category</Form.Label><Form.Select value={transactionFilters.categoryId} onChange={event => changeTransactionFilter('categoryId', event.target.value)}><option value="">All categories</option>{categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}</Form.Select></Form.Group>
        </div>
        {transactionError && <Alert variant="danger">{transactionError}</Alert>}
        {transactionsLoading ? <div className="text-center py-4"><Spinner animation="border" role="status"><span className="visually-hidden">Loading account transactions</span></Spinner></div> : accountTransactions && accountTransactions.items.length ? <>
          <Table responsive hover className="align-middle mb-2"><thead><tr><th>Date</th><th>Description</th><th>Category</th><th>Type</th><th className="text-end">Amount</th><th>Transfer</th></tr></thead><tbody>{accountTransactions.items.map(transaction => <tr key={transaction.id}><td className="text-nowrap">{displayDate(transaction.transactionDate)}</td><td><strong>{transaction.description}</strong>{transaction.subDescription && <div className="small text-secondary">{transaction.subDescription}</div>}</td><td>{transaction.categoryName ?? 'Uncategorized'}</td><td><Badge bg={transaction.transactionType === 'Transfer' ? 'info' : transaction.transactionType === 'Expense' ? 'danger' : transaction.transactionType === 'Income' ? 'success' : 'primary'} text={transaction.transactionType === 'Transfer' ? 'dark' : undefined}>{transaction.transactionType}</Badge></td><td className={`text-end text-nowrap ${transaction.amount < 0 ? 'text-danger' : 'text-success'}`}>{money(transaction.amount, transaction.currency)}</td><td>{transaction.linkedTransferCounterpart ? <Button size="sm" variant="outline-primary" onClick={() => navigate(`/transactions?focus=${transaction.linkedTransferCounterpart!.id}`)}>View counterpart</Button> : transaction.transactionType === 'Transfer' ? <Badge bg="light" text="dark">Unlinked</Badge> : '—'}</td></tr>)}</tbody></Table>
          <div className="d-flex justify-content-between align-items-center"><span className="small text-secondary">{accountTransactions.totalCount} transactions</span><div className="d-flex gap-2"><Button size="sm" variant="outline-secondary" disabled={accountTransactions.page <= 1} onClick={() => changeTransactionFilter('page', String(accountTransactions.page - 1))}>Previous</Button><span className="small align-self-center">Page {accountTransactions.page} of {Math.max(1, accountTransactions.totalPages)}</span><Button size="sm" variant="outline-secondary" disabled={accountTransactions.page >= accountTransactions.totalPages} onClick={() => changeTransactionFilter('page', String(accountTransactions.page + 1))}>Next</Button></div></div>
        </> : <div className="chart-empty">No transactions match this account and filter.</div>}
      </div> : null}

      {!selectedAccount && <div className="content-card p-3 p-md-4">
        <div className="d-flex justify-content-between align-items-center gap-3 mb-3"><div><h2 className="section-title">Recent transactions</h2><p className="small text-secondary mb-0">Saved transactions in the selected period</p></div><Link to="/transactions" className="btn btn-outline-dark btn-sm">View all</Link></div>
        {overview.recentTransactions.length === 0 ? <div className="empty-state"><div className="empty-icon">↔</div><h3>{hasTransactions ? 'No transactions in this period' : 'No saved transactions yet'}</h3><p className="text-secondary mb-3">The dashboard uses saved transactions only. CSV previews are not included.</p><Link to="/transactions" className="btn btn-outline-dark">Add a transaction manually</Link></div> : <Table responsive hover className="align-middle mb-0">
          <thead><tr><th>Date</th><th>Description</th><th>Account</th><th>Category</th><th className="text-end">Amount</th><th>Type</th></tr></thead><tbody>{overview.recentTransactions.map(transaction => <RecentTransactionRow key={transaction.id} transaction={transaction} />)}</tbody>
        </Table>}
      </div>}
    </>}
  </>;
}
