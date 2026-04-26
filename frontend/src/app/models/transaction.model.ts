export interface Transaction {
  id: number;
  description: string;
  amount: number;
  type: 'income' | 'expense';
  category: string;
  date: string;
  createdAt: string;
  classification?: 'Need' | 'Want' | 'Savings' | null;
}
