import { PeriodInfo } from './summary.model';

export interface BudgetPercentages {
  needs: number;
  wants: number;
  savings: number;
}

export interface BucketUsage {
  capBase: number;
  carryIn: number;
  capEffective: number;
  spent: number;
  pctUsed: number;
  status: 'ok' | 'warn' | 'over';
}

export interface BucketsUsage {
  needs: BucketUsage;
  wants: BucketUsage;
  savings: BucketUsage;
}

export interface CategoryCapUsage {
  categoryId: number;
  name: string;
  classification: 'Need' | 'Want' | 'Savings' | null;
  monthlyCap: number;
  spent: number;
  pctUsed: number;
  status: 'ok' | 'warn' | 'over';
}

export interface BudgetSnapshot {
  period: PeriodInfo;
  expectedIncome: number;
  expectedIncomeIsExplicit: boolean;
  percentages: BudgetPercentages;
  buckets: BucketsUsage;
  categoryCaps: CategoryCapUsage[];
}

export interface BudgetUpdate {
  expectedIncome?: number | null;
  percentages?: BudgetPercentages | null;
}

export interface CategoryBudgetEntry {
  categoryId: number;
  name: string;
  defaultClassification: 'Need' | 'Want' | 'Savings' | null;
  classification: 'Need' | 'Want' | 'Savings' | null;
  monthlyCap: number | null;
  hasOverride: boolean;
}

export interface CategoryBudgetUpdate {
  classification?: 'Need' | 'Want' | 'Savings' | null;
  monthlyCap?: number | null;
}
