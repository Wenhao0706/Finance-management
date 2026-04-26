import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { CategoryBudgetEntry } from '../../models/budget.model';

interface RowState {
  entry: CategoryBudgetEntry;
  classificationInput: 'Need' | 'Want' | 'Savings';
  capInput: string;
  saving: boolean;
  saved: boolean;
}

@Component({
  selector: 'app-category-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './category-settings.component.html',
  styleUrl: './category-settings.component.scss',
})
export class CategorySettingsComponent implements OnInit {
  rows = signal<RowState[]>([]);
  loading = signal(true);
  loadError = signal('');

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.getCategoryBudgets().subscribe({
      next: (entries) => {
        this.rows.set(entries.map(e => ({
          entry: e,
          classificationInput: (e.classification ?? 'Want') as 'Need' | 'Want' | 'Savings',
          capInput: e.monthlyCap !== null ? String(e.monthlyCap) : '',
          saving: false,
          saved: false,
        })));
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Could not load category settings.');
        this.loading.set(false);
      },
    });
  }

  saveRow(row: RowState): void {
    row.saving = true;
    row.saved = false;
    const cap = row.capInput.trim() === '' ? null : Number(row.capInput);
    if (cap !== null && (isNaN(cap) || cap < 0)) {
      row.saving = false;
      return;
    }
    this.api.updateCategoryBudget(row.entry.categoryId, {
      classification: row.classificationInput,
      monthlyCap: cap,
    }).subscribe({
      next: (updated) => {
        row.entry = updated;
        row.saving = false;
        row.saved = true;
        setTimeout(() => row.saved = false, 2000);
      },
      error: () => { row.saving = false; },
    });
  }
}
