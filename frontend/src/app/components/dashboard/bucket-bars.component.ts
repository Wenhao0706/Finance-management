import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BudgetSnapshot, BucketUsage } from '../../models/budget.model';

@Component({
  selector: 'app-bucket-bars',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './bucket-bars.component.html',
  styleUrl: './bucket-bars.component.scss',
})
export class BucketBarsComponent {
  @Input({ required: true }) budget!: BudgetSnapshot;

  bucketEntries(): { name: string; usage: BucketUsage }[] {
    return [
      { name: 'Needs', usage: this.budget.buckets.needs },
      { name: 'Wants', usage: this.budget.buckets.wants },
      { name: 'Savings', usage: this.budget.buckets.savings },
    ];
  }

  // Cap the visual bar at 100% width (overflow shown numerically)
  visualPct(usage: BucketUsage): number {
    return Math.min(usage.pctUsed, 100);
  }
}
