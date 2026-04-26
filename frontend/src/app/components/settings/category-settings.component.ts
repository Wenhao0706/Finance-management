import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ApiService } from '../../services/api.service';
import { CategoryBudgetEntry } from '../../models/budget.model';

interface RowState {
  entry: CategoryBudgetEntry;
  // Source of truth for what the user has typed; compared against `entry`
  // to derive `dirty` for the batched save flow.
  classificationInput: 'Need' | 'Want' | 'Savings';
  capInput: string;
  rowError: string | null;
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
  saving = signal(false);
  savedFlash = signal(false);
  saveError = signal('');

  // Number of rows whose inputs differ from the saved snapshot — used to
  // gate the Save button and show "X changes pending" copy.
  dirtyCount = computed(() => this.rows().filter(r => this.isRowDirty(r)).length);

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.getCategoryBudgets().subscribe({
      next: (entries) => {
        this.rows.set(entries.map(e => this.toRow(e)));
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Could not load your categories. Please refresh the page.');
        this.loading.set(false);
      },
    });
  }

  private toRow(e: CategoryBudgetEntry): RowState {
    return {
      entry: e,
      classificationInput: (e.classification ?? 'Want') as 'Need' | 'Want' | 'Savings',
      capInput: e.monthlyCap !== null ? String(e.monthlyCap) : '',
      rowError: null,
    };
  }

  isRowDirty(r: RowState): boolean {
    const savedClass = (r.entry.classification ?? 'Want');
    if (r.classificationInput !== savedClass) return true;
    const trimmed = r.capInput.trim();
    const savedCap = r.entry.monthlyCap !== null ? String(r.entry.monthlyCap) : '';
    return trimmed !== savedCap;
  }

  // Validates a row's cap value; returns the parsed value or an Error
  // marker (string) for inline display.
  private parseCap(r: RowState): { ok: true; value: number | null } | { ok: false; error: string } {
    const trimmed = r.capInput.trim();
    if (trimmed === '') return { ok: true, value: null };
    const n = Number(trimmed);
    if (isNaN(n) || n < 0) return { ok: false, error: 'Cap must be 0 or more' };
    return { ok: true, value: n };
  }

  saveAll(): void {
    if (this.saving()) return;

    const rows = this.rows();
    const dirtyRows: { row: RowState; cap: number | null }[] = [];

    // First pass — validate every dirty row before firing any requests.
    let hasError = false;
    for (const r of rows) {
      r.rowError = null;
      if (!this.isRowDirty(r)) continue;
      const parsed = this.parseCap(r);
      if (!parsed.ok) {
        r.rowError = parsed.error;
        hasError = true;
      } else {
        dirtyRows.push({ row: r, cap: parsed.value });
      }
    }
    this.rows.set([...rows]);   // trigger CD for rowError display

    if (hasError) {
      this.saveError.set('Please fix the highlighted rows.');
      return;
    }
    if (dirtyRows.length === 0) return;

    this.saving.set(true);
    this.saveError.set('');
    this.savedFlash.set(false);

    const calls = dirtyRows.map(({ row, cap }) =>
      this.api.updateCategoryBudget(row.entry.categoryId, {
        classification: row.classificationInput,
        monthlyCap: cap,
      }).pipe(catchError(err => of({ __error: true, row, err })))
    );

    forkJoin(calls).subscribe({
      next: (results) => {
        const failures: { row: RowState; err: any }[] = [];
        const next = [...this.rows()];
        for (const r of results) {
          if (r && (r as any).__error) {
            failures.push(r as any);
          } else {
            // Replace the entry in our list with the updated one
            const updated = r as CategoryBudgetEntry;
            const idx = next.findIndex(x => x.entry.categoryId === updated.categoryId);
            if (idx >= 0) {
              next[idx] = this.toRow(updated);
            }
          }
        }
        this.rows.set(next);
        this.saving.set(false);

        if (failures.length === 0) {
          this.savedFlash.set(true);
          setTimeout(() => this.savedFlash.set(false), 2400);
        } else {
          this.saveError.set(`Saved ${dirtyRows.length - failures.length} of ${dirtyRows.length} rows. Some changes did not save — please try again.`);
          for (const f of failures) f.row.rowError = 'Could not save this row';
        }
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set('Could not save your changes. Please try again.');
      },
    });
  }

  // "Reset" reverts every row's inputs to the saved snapshot.
  resetAll(): void {
    if (this.saving()) return;
    const next = this.rows().map(r => this.toRow(r.entry));
    this.rows.set(next);
    this.saveError.set('');
  }

  trackById(_index: number, row: RowState): number {
    return row.entry.categoryId;
  }
}
