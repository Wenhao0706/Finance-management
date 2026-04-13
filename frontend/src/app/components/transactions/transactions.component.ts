import { Component, OnInit } from '@angular/core';
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
  loading = true;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadTransactions();
  }

  loadTransactions(): void {
    this.loading = true;
    this.api.getTransactions().subscribe({
      next: (data) => {
        this.transactions = data;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      },
    });
  }

  // Optimistic delete: remove from the list immediately. If the API call fails,
  // restore the row at its original position so the user sees the rollback.
  deleteTransaction(t: Transaction): void {
    if (!confirm(`Delete "${t.description}"? This cannot be undone.`)) return;

    const index = this.transactions.indexOf(t);
    if (index === -1) return;
    this.transactions = this.transactions.filter((x) => x !== t);

    this.api.deleteTransaction(t.id).subscribe({
      error: () => {
        // Restore at original position (best-effort — order may have shifted
        // if the user added something while this was in flight, but that's a
        // tolerable edge case).
        const restored = [...this.transactions];
        restored.splice(index, 0, t);
        this.transactions = restored;
        alert('Could not delete that transaction. Please try again.');
      },
    });
  }
}
