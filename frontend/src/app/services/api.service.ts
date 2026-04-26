import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Transaction } from '../models/transaction.model';
import { Category } from '../models/category.model';
import { PeriodSummary } from '../models/summary.model';
import { BudgetSnapshot, BudgetUpdate, CategoryBudgetEntry, CategoryBudgetUpdate } from '../models/budget.model';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getTransactions(): Observable<Transaction[]> {
    return this.http.get<Transaction[]>(`${this.baseUrl}/transactions`);
  }

  getTransaction(id: number): Observable<Transaction> {
    return this.http.get<Transaction>(`${this.baseUrl}/transactions/${id}`);
  }

  createTransaction(transaction: Partial<Transaction>): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.baseUrl}/transactions`, transaction);
  }

  updateTransaction(id: number, transaction: Transaction): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/transactions/${id}`, transaction);
  }

  deleteTransaction(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/transactions/${id}`);
  }

  getCurrentMonthSummary(): Observable<PeriodSummary> {
    return this.http.get<PeriodSummary>(`${this.baseUrl}/transactions/summary`);
  }

  getMonthSummary(year: number, month: number): Observable<PeriodSummary> {
    return this.http.get<PeriodSummary>(
      `${this.baseUrl}/transactions/summary?year=${year}&month=${month}`);
  }

  getYearSummary(year: number): Observable<PeriodSummary> {
    return this.http.get<PeriodSummary>(
      `${this.baseUrl}/transactions/summary?year=${year}`);
  }

  getCategories(): Observable<Category[]> {
    return this.http.get<Category[]>(`${this.baseUrl}/categories`);
  }

  getBudget(year: number, month: number): Observable<BudgetSnapshot> {
    return this.http.get<BudgetSnapshot>(`${this.baseUrl}/budgets/${year}/${month}`);
  }

  updateBudget(year: number, month: number, update: BudgetUpdate): Observable<BudgetSnapshot> {
    return this.http.put<BudgetSnapshot>(`${this.baseUrl}/budgets/${year}/${month}`, update);
  }

  getCategoryBudgets(): Observable<CategoryBudgetEntry[]> {
    return this.http.get<CategoryBudgetEntry[]>(`${this.baseUrl}/category-budgets`);
  }

  updateCategoryBudget(categoryId: number, update: CategoryBudgetUpdate): Observable<CategoryBudgetEntry> {
    return this.http.put<CategoryBudgetEntry>(`${this.baseUrl}/category-budgets/${categoryId}`, update);
  }
}
