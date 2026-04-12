export interface Transaction {
  id: number;
  description: string;
  amount: number;
  type: 'income' | 'expense';
  category: string;
  date: string;
  createdAt: string;
}

export interface Summary {
  totalIncome: number;
  totalExpense: number;
  balance: number;
}
