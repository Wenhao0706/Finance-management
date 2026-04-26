import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { BudgetSnapshot } from '../../models/budget.model';

@Component({
  selector: 'app-budget-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './budget-settings.component.html',
  styleUrl: './budget-settings.component.scss',
})
export class BudgetSettingsComponent implements OnInit {
  snapshot: BudgetSnapshot | null = null;
  loading = signal(true);
  saving = signal(false);
  error = signal('');
  saved = signal(false);

  year!: number;
  month!: number;
  expectedIncomeInput: string = '';   // empty = derive
  needsPctInput: number = 50;
  wantsPctInput: number = 30;
  savingsPctInput: number = 20;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    const now = new Date();
    this.year = now.getFullYear();
    this.month = now.getMonth() + 1;
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.api.getBudget(this.year, this.month).subscribe({
      next: (s) => {
        this.snapshot = s;
        this.expectedIncomeInput = s.expectedIncomeIsExplicit ? String(s.expectedIncome) : '';
        this.needsPctInput = Math.round(s.percentages.needs * 100);
        this.wantsPctInput = Math.round(s.percentages.wants * 100);
        this.savingsPctInput = Math.round(s.percentages.savings * 100);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load budget. Try again.');
        this.loading.set(false);
      },
    });
  }

  get pctSum(): number {
    return this.needsPctInput + this.wantsPctInput + this.savingsPctInput;
  }

  save(): void {
    if (this.pctSum !== 100) {
      this.error.set('Percentages must sum to 100.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.saved.set(false);

    const expectedIncome = this.expectedIncomeInput.trim() === ''
      ? null
      : Number(this.expectedIncomeInput);
    if (expectedIncome !== null && (isNaN(expectedIncome) || expectedIncome < 0)) {
      this.error.set('Expected income must be a non-negative number, or blank to auto-derive.');
      this.saving.set(false);
      return;
    }

    this.api.updateBudget(this.year, this.month, {
      expectedIncome,
      percentages: {
        needs: this.needsPctInput / 100,
        wants: this.wantsPctInput / 100,
        savings: this.savingsPctInput / 100,
      },
    }).subscribe({
      next: (s) => {
        this.snapshot = s;
        this.saving.set(false);
        this.saved.set(true);
        setTimeout(() => this.saved.set(false), 2000);
      },
      error: (err) => {
        this.error.set(err?.error?.error ?? 'Could not save budget.');
        this.saving.set(false);
      },
    });
  }

  get derivedPlaceholder(): string {
    if (!this.snapshot || this.snapshot.expectedIncomeIsExplicit) return '';
    return `Auto: ${this.snapshot.expectedIncome.toFixed(2)} (last month's actual)`;
  }
}
