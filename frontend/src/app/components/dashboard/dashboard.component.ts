import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { PeriodSummary } from '../../models/summary.model';
import { Transaction } from '../../models/transaction.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  summary: PeriodSummary | null = null;
  recentTransactions: Transaction[] | null = null;
  loading = signal(true);
  loadError = signal('');
  userFirstName = '';

  year!: number;
  month!: number;

  // Categories get a stable colour mapping shared with the recent-activity
  // dot indicator and the donut chart. Names are matched case-insensitively.
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
  };

  constructor(
    private api: ApiService,
    private auth: AuthService,
    private route: ActivatedRoute,
    private router: Router,
  ) {}

  ngOnInit(): void {
    this.auth.currentUser$.subscribe(u => {
      const name = u?.displayName ?? u?.email ?? '';
      this.userFirstName = (name.split(/[\s@]/)[0]) || 'there';
    });
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
      month: 'long', year: 'numeric',
    });
  }

  get todayLabel(): string {
    return new Date().toLocaleString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  // Net flow status — for the page-meta sentence under the title.
  get netStatus(): 'positive' | 'negative' | 'zero' {
    if (!this.summary) return 'zero';
    if (this.summary.netFlow > 0) return 'positive';
    if (this.summary.netFlow < 0) return 'negative';
    return 'zero';
  }
  get absoluteNet(): number {
    return Math.abs(this.summary?.netFlow ?? 0);
  }

  // Income / expense subtitle bits
  get inSubLabel(): string {
    if (!this.recentTransactions) return '';
    const ins = this.recentTransactions.filter(t => t.type === 'income').length;
    return ins === 0 ? 'no income recorded yet'
         : `${ins} ${ins === 1 ? 'deposit' : 'deposits'}`;
  }
  get outSubLabel(): string {
    if (!this.summary) return '';
    const expensesCount = this.recentTransactions?.filter(t => t.type === 'expense').length ?? 0;
    return expensesCount === 0
      ? 'nothing spent yet'
      : `${expensesCount} ${expensesCount === 1 ? 'transaction' : 'transactions'}`;
  }

  // Saved this month = positive net flow only
  get savedAmount(): number {
    if (!this.summary) return 0;
    return Math.max(0, this.summary.netFlow);
  }
  get savingsRatePct(): number | null {
    if (!this.summary || this.summary.income === 0) return null;
    return Math.round((this.savedAmount / this.summary.income) * 100);
  }

  // Donut chart math.
  // Circumference = 2π·80 ≈ 502.65. Each segment's stroke-dasharray
  // is the bucket's used-share of the circle, and the offset stacks
  // it after the previous segment.
  private static readonly CIRCUMFERENCE = 502.65;
  private bucketShare(spent: number): number {
    if (!this.summary?.budget) return 0;
    const totalCap =
      this.summary.budget.buckets.needs.capEffective +
      this.summary.budget.buckets.wants.capEffective +
      this.summary.budget.buckets.savings.capEffective;
    if (totalCap === 0) return 0;
    return spent / totalCap;
  }

  get needsDash(): string {
    const len = this.bucketShare(this.summary?.budget?.buckets.needs.spent ?? 0) * DashboardComponent.CIRCUMFERENCE;
    return `${len.toFixed(2)} ${DashboardComponent.CIRCUMFERENCE}`;
  }
  get wantsDash(): string {
    const len = this.bucketShare(this.summary?.budget?.buckets.wants.spent ?? 0) * DashboardComponent.CIRCUMFERENCE;
    return `${len.toFixed(2)} ${DashboardComponent.CIRCUMFERENCE}`;
  }
  get wantsOffset(): number {
    const offset = this.bucketShare(this.summary?.budget?.buckets.needs.spent ?? 0) * DashboardComponent.CIRCUMFERENCE;
    return -offset;
  }
  get savingsDash(): string {
    const len = this.bucketShare(this.summary?.budget?.buckets.savings.spent ?? 0) * DashboardComponent.CIRCUMFERENCE;
    return `${len.toFixed(2)} ${DashboardComponent.CIRCUMFERENCE}`;
  }
  get savingsOffset(): number {
    const offset =
      (this.bucketShare(this.summary?.budget?.buckets.needs.spent ?? 0) +
       this.bucketShare(this.summary?.budget?.buckets.wants.spent ?? 0)) *
      DashboardComponent.CIRCUMFERENCE;
    return -offset;
  }

  // Center label of the donut — overall % of total caps used.
  get overallBucketPct(): number {
    if (!this.summary?.budget) return 0;
    const b = this.summary.budget.buckets;
    const totalSpent = b.needs.spent + b.wants.spent + b.savings.spent;
    const totalCap   = b.needs.capEffective + b.wants.capEffective + b.savings.capEffective;
    if (totalCap === 0) return 0;
    return Math.round((totalSpent / totalCap) * 100);
  }

  get wantsSubLabel(): string {
    const w = this.summary?.budget?.buckets.wants;
    if (!w) return '';
    if (w.status === 'over') return 'over budget';
    if (w.status === 'warn') return 'near limit — go gentle';
    return 'things you choose to spend';
  }

  categoryColor(name: string): string {
    return DashboardComponent.CATEGORY_COLORS[name?.toLowerCase()] ?? 'var(--color-text-muted)';
  }
}
