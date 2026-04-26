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
  styleUrl: './transactions.component.scss'
})
export class TransactionsComponent implements OnInit {
  transactions: Transaction[] = [];
  loading = signal(true);
  loadError = signal('');

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

  // Optimistic delete: remove from the list immediately. If the API call
  // fails, restore the row at its original position so the user sees the
  // rollback.
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
}
