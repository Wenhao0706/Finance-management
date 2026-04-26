import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { PeriodSummary } from '../../models/summary.model';
import { Transaction } from '../../models/transaction.model';
import { PeriodSummaryComponent } from './period-summary.component';
import { CategoryBarsComponent } from './category-bars.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, PeriodSummaryComponent, CategoryBarsComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  summary: PeriodSummary | null = null;
  recentTransactions: Transaction[] | null = null;
  loading = signal(true);
  loadError = signal('');

  year!: number;
  month!: number;

  constructor(
    private api: ApiService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const yearParam = params.get('year');
      const monthParam = params.get('month');
      const now = new Date();
      this.year = yearParam ? Number(yearParam) : now.getFullYear();
      this.month = monthParam ? Number(monthParam) : now.getMonth() + 1;
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    forkJoin({
      summary: this.api.getMonthSummary(this.year, this.month),
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

  goPrev(): void {
    let y = this.year, m = this.month - 1;
    if (m < 1) { m = 12; y -= 1; }
    this.router.navigate(['/dashboard/month', y, m]);
  }

  goNext(): void {
    let y = this.year, m = this.month + 1;
    if (m > 12) { m = 1; y += 1; }
    this.router.navigate(['/dashboard/month', y, m]);
  }

  goYearView(): void {
    this.router.navigate(['/dashboard/year', this.year]);
  }

  get monthLabel(): string {
    return new Date(this.year, this.month - 1, 1).toLocaleString('en-US', {
      month: 'long',
      year: 'numeric',
    });
  }
}
