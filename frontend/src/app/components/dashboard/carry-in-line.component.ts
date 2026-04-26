import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BudgetSnapshot } from '../../models/budget.model';

interface CarryPart {
  name: string;
  amount: number;
}

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

  // Returns one part per non-zero bucket carry-in; signed amount is kept
  // raw (negative = "owed" / overspent last month).
  detailedParts(): CarryPart[] {
    return [
      { name: 'Needs',   amount: this.budget.buckets.needs.carryIn },
      { name: 'Wants',   amount: this.budget.buckets.wants.carryIn },
      { name: 'Savings', amount: this.budget.buckets.savings.carryIn },
    ].filter(p => p.amount !== 0);
  }

  absValue(v: number): number {
    return Math.abs(v);
  }
}
