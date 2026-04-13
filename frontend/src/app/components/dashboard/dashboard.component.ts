import { Component, OnInit } from '@angular/core';
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
  summary: Summary | null = null;        // null while loading — prevents
  recentTransactions: Transaction[] | null = null;  // empty-state flash
  loading = true;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    // forkJoin so we flip `loading` only once both endpoints have responded —
    // avoids a half-rendered dashboard with cards loaded but transactions
    // blank (or vice versa).
    forkJoin({
      summary: this.api.getSummary(),
      transactions: this.api.getTransactions(),
    }).subscribe({
      next: ({ summary, transactions }) => {
        this.summary = summary;
        this.recentTransactions = transactions.slice(0, 5);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      },
    });
  }
}
