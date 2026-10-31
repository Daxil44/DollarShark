export type Account = {
  id: string;
  name: string;
  institution: string;
  accountType: string;
  classification: 'Asset' | 'Liability';
  currency: string;
  lastFourDigits: string | null;
  openingBalance: number | null;
  openingBalanceDate: string | null;
  calculatedBalance: number | null;
  isActive: boolean;
  createdAtUtc: string;
};

export type AccountInput = {
  name: string;
  institution: string;
  accountType: string;
  currency: string;
  lastFourDigits: string;
  openingBalance: string;
  openingBalanceDate: string;
};

export type Category = {
  id: string;
  name: string;
  parentCategoryId: string | null;
  parentCategoryName: string | null;
  transactionCount: number;
  childCount: number;
  isSystem: boolean;
};

export type CategoryInput = {
  name: string;
  parentCategoryId: string;
};

export type Transaction = {
  id: string;
  transactionDate: string;
  description: string;
  subDescription: string | null;
  amount: number;
  transactionType: string;
  currency: string;
  financialAccountId: string;
  accountName: string;
  categoryId: string | null;
  categoryName: string | null;
  createdAtUtc: string;
};

export type TransactionInput = {
  transactionDate: string;
  description: string;
  subDescription?: string | null;
  amount: number;
  transactionType: string;
  financialAccountId: string;
  categoryId: string | null;
};

export type TransactionPage = {
  items: Transaction[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type StatementImport = {
  id: string;
  financialAccountId: string;
  accountName: string;
  originalFileName: string;
  fileSizeBytes: number;
  contentType: string;
  status: string;
  uploadedAtUtc: string;
  processedAtUtc: string | null;
  statementType: 'Bank' | 'CreditCard';
  importedTransactionCount: number;
};

export type StatementImportPage = {
  items: StatementImport[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type StatementCsvCandidate = {
  sourceRowNumber: number;
  transactionDate: string | null;
  postedDate: string | null;
  description: string;
  amount: number | null;
  currency: string | null;
  referenceNumber: string | null;
  validationMessages: string[];
  status: 'Valid' | 'Invalid';
  duplicateCheck?: DuplicateCheck | null;
  subDescription?: string | null;
  sourceTransactionType?: string | null;
  sourceAmount?: number | null;
  suggestedTransactionType?: string | null;
};

export type DuplicateCheck = {
  classification: 'NoMatch' | 'LikelyDuplicate' | 'PossibleDuplicate';
  existingTransactionId: string | null;
  existingTransactionDate: string | null;
  existingDescription: string | null;
  existingAmount: number | null;
  existingCurrency: string | null;
  existingAccountName: string | null;
  reason: string;
};

export type StatementCsvPreview = {
  candidates: StatementCsvCandidate[];
  validRows: number;
  invalidRows: number;
};

export type StatementImportCandidateChange = {
  sourceRowNumber: number;
  selected: boolean;
  transactionDate: string | null;
  description: string;
  subDescription: string | null;
  amount: number | null;
  transactionType: string;
  categoryId: string | null;
  duplicateDecision: 'Skip' | 'ImportAnyway' | 'Unresolved' | null;
  reviewedDuplicateTransactionId: string | null;
  reviewedDuplicateClassification: DuplicateCheck['classification'] | null;
};

export type StatementImportConfirmationResult = {
  createdCount: number;
  excludedCount: number;
  rejectedCount: number;
  rejectedRows: { sourceRowNumber: number; messages: string[]; duplicateCheck?: DuplicateCheck | null }[];
};

export type DashboardCurrencySummary = {
  currency: string;
  income: number;
  expenses: number;
  netCashFlow: number;
  previousIncome: number;
  previousExpenses: number;
  previousNetCashFlow: number;
};

export type DashboardCategoryExpense = {
  currency: string;
  categoryName: string;
  amount: number;
};

export type DashboardMonthlyTotals = {
  currency: string;
  month: string;
  income: number;
  expenses: number;
};

export type DashboardRecentTransaction = {
  id: string;
  transactionDate: string;
  description: string;
  amount: number;
  currency: string;
  transactionType: string;
  accountName: string;
  categoryName: string | null;
};

export type DashboardOverview = {
  periodStart: string;
  periodEnd: string;
  previousPeriodStart: string;
  previousPeriodEnd: string;
  currencies: DashboardCurrencySummary[];
  recentTransactions: DashboardRecentTransaction[];
  categoryExpenses: DashboardCategoryExpense[];
  monthlyTotals: DashboardMonthlyTotals[];
};
