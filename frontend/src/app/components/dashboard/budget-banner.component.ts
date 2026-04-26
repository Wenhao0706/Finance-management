import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BudgetSnapshot } from '../../models/budget.model';

@Component({
  selector: 'app-budget-banner',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './budget-banner.component.html',
  styleUrl: './budget-banner.component.scss',
})
export class BudgetBannerComponent {
  @Input({ required: true }) budget!: BudgetSnapshot;

  // Tier:
  //   "over" if any bucket or capped category is over (≥100%)
  //   "warn" if any is in warn (80-99%)
  //   null if none
  get tier(): 'over' | 'warn' | null {
    const statuses = [
      this.budget.buckets.needs.status,
      this.budget.buckets.wants.status,
      this.budget.buckets.savings.status,
      ...this.budget.categoryCaps.map(c => c.status),
    ];
    if (statuses.includes('over')) return 'over';
    if (statuses.includes('warn')) return 'warn';
    return null;
  }

  get message(): string {
    if (this.tier === 'over') return 'You are over budget on at least one bucket or category this month.';
    if (this.tier === 'warn') return 'You are approaching your budget limit on at least one bucket or category.';
    return '';
  }
}
