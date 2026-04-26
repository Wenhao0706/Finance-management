import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BudgetSnapshot } from '../../models/budget.model';

@Component({
  selector: 'app-carry-in-line',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './carry-in-line.component.html',
  styleUrl: './carry-in-line.component.scss',
})
export class CarryInLineComponent {
  @Input({ required: true }) budget!: BudgetSnapshot;

  hasAnyCarryIn(): boolean {
    return this.budget.buckets.needs.carryIn !== 0
        || this.budget.buckets.wants.carryIn !== 0
        || this.budget.buckets.savings.carryIn !== 0;
  }

  parts(): string[] {
    const fmt = (name: string, v: number) => {
      if (v === 0) return null;
      const sign = v > 0 ? '+' : '';
      return `${sign}${v.toFixed(2)} ${name}`;
    };
    const items = [
      fmt('Needs', this.budget.buckets.needs.carryIn),
      fmt('Wants', this.budget.buckets.wants.carryIn),
      fmt('Savings', this.budget.buckets.savings.carryIn),
    ].filter((s): s is string => s !== null);
    return items;
  }
}
