export type Account = {
  id: string;
  name: string;
  institution: string;
  accountType: string;
  currency: string;
  lastFourDigits: string | null;
  isActive: boolean;
  createdAtUtc: string;
};

export type AccountInput = {
  name: string;
  institution: string;
  accountType: string;
  currency: string;
  lastFourDigits: string;
};

export type Category = {
  id: string;
  name: string;
  parentCategoryId: string | null;
  parentCategoryName: string | null;
  transactionCount: number;
  childCount: number;
};

export type CategoryInput = {
  name: string;
  parentCategoryId: string;
};

export type Transaction = {
  id: string;
  transactionDate: string;
  description: string;
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
