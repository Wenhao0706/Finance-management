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

  // Tracks which preset card is "Active" — derived from the percentages
  // when data loads, but also set explicitly when the user clicks a card.
  activePreset: 'balanced' | 'saver' | 'conservative' | 'custom' = 'balanced';

  constructor(private api: ApiService) {}

  applyPreset(p: 'balanced' | 'saver' | 'conservative' | 'custom'): void {
    this.activePreset = p;
    if (p === 'balanced')      { this.needsPctInput = 50; this.wantsPctInput = 30; this.savingsPctInput = 20; }
    else if (p === 'saver')    { this.needsPctInput = 50; this.wantsPctInput = 20; this.savingsPctInput = 30; }
    else if (p === 'conservative') { this.needsPctInput = 60; this.wantsPctInput = 25; this.savingsPctInput = 15; }
    // 'custom' leaves the inputs as-is — user edits the numbers directly
  }

  private detectPreset(needs: number, wants: number, savings: number): typeof this.activePreset {
    if (needs === 50 && wants === 30 && savings === 20) return 'balanced';
    if (needs === 50 && wants === 20 && savings === 30) return 'saver';
    if (needs === 60 && wants === 25 && savings === 15) return 'conservative';
    return 'custom';
  }

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
        this.needsPctInput   = Math.round(s.percentages.needs   * 100);
        this.wantsPctInput   = Math.round(s.percentages.wants   * 100);
        this.savingsPctInput = Math.round(s.percentages.savings * 100);
        this.activePreset = this.detectPreset(this.needsPctInput, this.wantsPctInput, this.savingsPctInput);
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
    if (this.snapshot.expectedIncome === 0) return 'e.g. 3,000.00';
    return `${this.snapshot.expectedIncome.toFixed(2)} (from last month)`;
  }

  // Long-form, human-readable period label, e.g. "April 2026".
  get humanPeriod(): string {
    if (!this.snapshot) return '';
    const m = this.snapshot.period.month ?? this.month;
    const y = this.snapshot.period.year;
    return new Date(y, m - 1, 1).toLocaleString('en-US', { month: 'long', year: 'numeric' });
  }
}
