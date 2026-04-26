export interface PeriodInfo {
  year: number;
  month?: number;  // omitted = yearly
}

export interface CategoryAmount {
  category: string;
  amount: number;
  percentage: number;
}

export interface MonthlyEntry {
  month: number;
  income: number;
  expenses: number;
  savings: number;
  netFlow: number;
}

export interface PeriodSummary {
  period: PeriodInfo;
  income: number;
  expenses: number;
  savings: number;
  netFlow: number;
  runningBalance: number;
  categoryBreakdown: CategoryAmount[];
  monthlyBreakdown?: MonthlyEntry[];
}
