import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PeriodSummary } from '../../models/summary.model';

@Component({
  selector: 'app-period-summary',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './period-summary.component.html',
  styleUrl: './period-summary.component.scss',
})
export class PeriodSummaryComponent {
  @Input({ required: true }) summary!: PeriodSummary;

  get isYearly(): boolean {
    return this.summary.period.month === undefined || this.summary.period.month === null;
  }

  get savingsRate(): number | null {
    if (this.summary.income === 0) return null;
    return Math.round(this.summary.savings / this.summary.income * 1000) / 10;
  }
}
