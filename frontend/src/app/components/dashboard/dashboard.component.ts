import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { Summary } from '../../models/transaction.model';
import { Transaction } from '../../models/transaction.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  summary: Summary | null = null;
  recentTransactions: Transaction[] | null = null;
  loading = signal(true);
  loadError = signal('');

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    // forkJoin so the dashboard renders all-at-once instead of one card at
    // a time (which looked janky when summary was fast and transactions
    // were slow).
    forkJoin({
      summary: this.api.getSummary(),
      transactions: this.api.getTransactions(),
    }).subscribe({
      next: ({ summary, transactions }) => {
        this.summary = summary;
        this.recentTransactions = transactions.slice(0, 5);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        const status = err?.status;
        if (status === 0)        this.loadError.set('Network error — check your connection.');
        else if (status === 401) this.loadError.set('Your session expired. Please sign in again.');
        else if (status >= 500)  this.loadError.set('Server error. Try again in a moment.');
        else                     this.loadError.set('Could not load your dashboard.');
      },
    });
  }
}
