import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { PeriodSummary, MonthlyEntry } from '../../models/summary.model';
import { PeriodSummaryComponent } from './period-summary.component';

@Component({
  selector: 'app-yearly-dashboard',
  standalone: true,
  imports: [CommonModule, PeriodSummaryComponent],
  templateUrl: './yearly-dashboard.component.html',
  styleUrl: './yearly-dashboard.component.scss',
})
export class YearlyDashboardComponent implements OnInit {
  summary: PeriodSummary | null = null;
  loading = signal(true);
  loadError = signal('');

  year!: number;

  constructor(
    private api: ApiService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const yearParam = params.get('year');
      this.year = yearParam ? Number(yearParam) : new Date().getFullYear();
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.getYearSummary(this.year).subscribe({
      next: (s) => {
        this.summary = s;
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        const status = err?.status;
        if (status === 0)        this.loadError.set('Network error — check your connection.');
        else if (status === 401) this.loadError.set('Your session expired. Please sign in again.');
        else if (status >= 500)  this.loadError.set('Server error. Try again in a moment.');
        else                     this.loadError.set('Could not load your yearly summary.');
      },
    });
  }

  goPrevYear(): void {
    this.router.navigate(['/dashboard/year', this.year - 1]);
  }

  goNextYear(): void {
    this.router.navigate(['/dashboard/year', this.year + 1]);
  }

  goMonthView(): void {
    this.router.navigate(['/dashboard']);
  }

  goToMonth(month: number): void {
    this.router.navigate(['/dashboard/month', this.year, month]);
  }

  monthName(month: number): string {
    return new Date(2000, month - 1, 1).toLocaleString('en-US', { month: 'short' });
  }
}
