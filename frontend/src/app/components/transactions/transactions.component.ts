import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { Transaction } from '../../models/transaction.model';

@Component({
  selector: 'app-transactions',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './transactions.component.html',
  styleUrl: './transactions.component.scss',
})
export class TransactionsComponent implements OnInit {
  transactions: Transaction[] = [];
  loading = signal(true);
  loadError = signal('');

  // Same colour map used by the dashboard activity stream — kept in sync
  // here so the small dot before each category name matches.
  private static readonly CATEGORY_COLORS: Record<string, string> = {
    'food & dining':  'var(--color-amber)',
    'food':           'var(--color-amber)',
    'transportation': 'var(--color-indigo)',
    'transport':      'var(--color-indigo)',
    'income':         'var(--color-mint)',
    'salary':         'var(--color-mint)',
    'entertainment':  'var(--color-magenta)',
    'utilities':      'var(--color-cyan)',
    'healthcare':     'var(--color-coral)',
    'housing':        '#60a5fa',
    'shopping':       '#a78bfa',
    'savings':        'var(--color-mint)',
    'freelance':      '#fb923c',
    'investments':    '#22c55e',
  };

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadTransactions();
  }

  loadTransactions(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.getTransactions().subscribe({
      next: (data) => {
        this.transactions = data;
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        const status = err?.status;
        if (status === 0)        this.loadError.set('Network error — check your connection.');
        else if (status === 401) this.loadError.set('Your session expired. Please sign in again.');
        else if (status >= 500)  this.loadError.set('Server error. Try again in a moment.');
        else                     this.loadError.set('Could not load transactions.');
      },
    });
  }

  deleteTransaction(t: Transaction): void {
    if (!confirm(`Are you sure you want to delete "${t.description}"?\n\nThis cannot be undone.`)) return;
    const index = this.transactions.indexOf(t);
    if (index === -1) return;
    this.transactions = this.transactions.filter((x) => x !== t);
    this.api.deleteTransaction(t.id).subscribe({
      error: () => {
        const restored = [...this.transactions];
        restored.splice(index, 0, t);
        this.transactions = restored;
        alert('Sorry — we could not delete that transaction. Please try again.');
      },
    });
  }

  categoryColor(name: string): string {
    return TransactionsComponent.CATEGORY_COLORS[name?.toLowerCase()] ?? 'var(--color-text-muted)';
  }

  trackById(_i: number, t: Transaction): number | string {
    return t.id;
  }

  // Summary pills above the list
  get totalIn(): number {
    return this.transactions.filter(t => t.type === 'income').reduce((s, t) => s + t.amount, 0);
  }
  get totalOut(): number {
    return this.transactions.filter(t => t.type === 'expense').reduce((s, t) => s + t.amount, 0);
  }
  get totalNet(): number { return this.totalIn - this.totalOut; }
  get absNet(): number   { return Math.abs(this.totalNet); }

  get todayLabel(): string {
    return new Date().toLocaleString('en-US', { month: 'long', year: 'numeric' });
  }
}
