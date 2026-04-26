import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
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

  // Inline category icons — duplicated from transaction-form so both pages
  // render identical glyphs without coupling to a shared module yet.
  private readonly ICONS: Record<string, string> = {
    'food & dining':  '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 2v7c0 1.1.9 2 2 2h4a2 2 0 0 0 2-2V2"/><path d="M7 2v20"/><path d="M21 15V2a5 5 0 0 0-5 5v6c0 1.1.9 2 2 2h3Zm0 0v7"/></svg>',
    'food':           '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 2v7c0 1.1.9 2 2 2h4a2 2 0 0 0 2-2V2"/><path d="M7 2v20"/><path d="M21 15V2a5 5 0 0 0-5 5v6c0 1.1.9 2 2 2h3Zm0 0v7"/></svg>',
    'transportation': '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 16H9m10 0h3v-3.15a1 1 0 0 0-.84-.99L16 11l-2.7-3.6a1 1 0 0 0-.8-.4H5.24a2 2 0 0 0-1.8 1.1l-.8 1.63A6 6 0 0 0 2 12.42V16h2"/><circle cx="6.5" cy="16.5" r="2.5"/><circle cx="16.5" cy="16.5" r="2.5"/></svg>',
    'transport':      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 16H9m10 0h3v-3.15a1 1 0 0 0-.84-.99L16 11l-2.7-3.6a1 1 0 0 0-.8-.4H5.24a2 2 0 0 0-1.8 1.1l-.8 1.63A6 6 0 0 0 2 12.42V16h2"/><circle cx="6.5" cy="16.5" r="2.5"/><circle cx="16.5" cy="16.5" r="2.5"/></svg>',
    'housing':        '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>',
    'utilities':      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>',
    'healthcare':     '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.29 1.51 4.04 3 5.5l7 7Z"/><path d="M3.22 12H9.5l.5-1 2 4.5 2-7 1.5 3.5h5.27"/></svg>',
    'entertainment':  '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M7 3v18M3 7.5h4M3 12h18M3 16.5h4M17 3v18M17 7.5h4M17 16.5h4"/></svg>',
    'shopping':       '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z"/><line x1="3" x2="21" y1="6" y2="6"/><path d="M16 10a4 4 0 0 1-8 0"/></svg>',
    'savings':        '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 5c-1.5 0-2.8 1.4-3 2-3.5-1.5-11-.3-11 5 0 1.8 0 3 2 4.5V20h4v-2h3v2h4v-4c1-.5 1.7-1 2-2h2v-4h-2c0-1-.5-1.5-1-2V5z"/><circle cx="16" cy="11" r="0.7" fill="currentColor"/></svg>',
    'income':         '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="22 7 13.5 15.5 8.5 10.5 2 17"/><polyline points="16 7 22 7 22 13"/></svg>',
    'salary':         '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="22 7 13.5 15.5 8.5 10.5 2 17"/><polyline points="16 7 22 7 22 13"/></svg>',
    'freelance':      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="14" x="2" y="7" rx="2" ry="2"/><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/></svg>',
    'investments':    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 3v18h18"/><path d="M18 17V9"/><path d="M13 17V5"/><path d="M8 17v-3"/></svg>',
  };

  constructor(private api: ApiService, private sanitizer: DomSanitizer) {}

  categorySlug(name: string): string {
    const k = (name || '').toLowerCase();
    if (k.includes('food'))      return 'food';
    if (k.includes('transport')) return 'transport';
    if (k.includes('housing'))   return 'housing';
    if (k.includes('utilit'))    return 'utilities';
    if (k.includes('health'))    return 'healthcare';
    if (k.includes('entertain')) return 'entertainment';
    if (k.includes('shop'))      return 'shopping';
    if (k.includes('saving'))    return 'savings';
    if (k.includes('freelance')) return 'freelance';
    if (k.includes('invest'))    return 'investments';
    if (k.includes('salary') || k.includes('income')) return 'income';
    return 'other';
  }
  categoryIcon(name: string): SafeHtml {
    const k = (name || '').toLowerCase();
    const exact = this.ICONS[k];
    if (exact) return this.sanitizer.bypassSecurityTrustHtml(exact);
    const fuzzy = Object.keys(this.ICONS).find(key => k.includes(key));
    return this.sanitizer.bypassSecurityTrustHtml(fuzzy ? this.ICONS[fuzzy] : this.ICONS['shopping']);
  }

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
