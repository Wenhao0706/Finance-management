import { Component, ElementRef, OnInit, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ApiService } from '../../services/api.service';
import { Category } from '../../models/category.model';

type DateShortcut = 'today' | 'yesterday' | 'pick';

@Component({
  selector: 'app-transaction-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './transaction-form.component.html',
  styleUrl: './transaction-form.component.scss',
})
export class TransactionFormComponent implements OnInit {
  @ViewChild('datePickerEl') datePickerEl!: ElementRef<HTMLInputElement>;

  categories: Category[] = [];
  filteredCategories: Category[] = [];

  loadingCategories = signal(true);
  saving = signal(false);
  errorMessage = signal('');

  // The numeric amount lives in form.amount; the user-typed string
  // (with commas as they type) lives in amountInput. We keep them in
  // sync via onAmountInput().
  amountInput = '';
  hasAmountCents = false;

  form = {
    description: '',
    amount: null as number | null,
    type: 'expense' as 'income' | 'expense',
    category: '',
    date: new Date().toISOString().split('T')[0],
    classification: '' as 'Need' | 'Want' | 'Savings' | '',
  };

  dateShortcut: DateShortcut = 'today';

  readonly quickAmounts = [20, 50, 100, 200, 500, 1000];

  // Inline SVG strings keyed by category name (lower-case). Returned via
  // DomSanitizer so [innerHTML] doesn't strip them. Each viewBox is 24×24,
  // stroke=currentColor so the colour comes from the .cat-tile-icon parent.
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
    // Briefcase glyph for self-employed / contract income
    'freelance':      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="14" x="2" y="7" rx="2" ry="2"/><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/></svg>',
    // Bar-chart for investment income (distinct from the trending-up arrow used by salary/income)
    'investments':    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 3v18h18"/><path d="M18 17V9"/><path d="M13 17V5"/><path d="M8 17v-3"/></svg>',
  };

  constructor(
    private api: ApiService,
    public router: Router,
    private sanitizer: DomSanitizer,
  ) {}

  ngOnInit(): void {
    this.api.getCategories().subscribe({
      next: (data) => {
        this.categories = data;
        this.filterCategories();
        this.loadingCategories.set(false);
      },
      error: () => {
        this.loadingCategories.set(false);
        this.errorMessage.set('Could not load categories. Please refresh.');
      },
    });
  }

  // ===== Type
  setType(type: 'income' | 'expense'): void {
    if (this.form.type === type) return;
    this.form.type = type;
    if (type === 'income') this.form.classification = '';
    this.filterCategories();
  }

  filterCategories(): void {
    this.filteredCategories = this.categories.filter(c => c.type === this.form.type);
    if (!this.filteredCategories.find(c => c.name === this.form.category)) {
      this.form.category = this.filteredCategories[0]?.name ?? '';
    }
  }

  // ===== Amount handling — type="text" + comma formatter so the native
  // number-spinner doesn't appear and large numbers stay readable.
  onAmountInput(): void {
    const raw = (this.amountInput ?? '').replace(/[^0-9.]/g, '');
    // Keep only the first decimal point
    const firstDot = raw.indexOf('.');
    let normalized = raw;
    if (firstDot !== -1) {
      normalized = raw.slice(0, firstDot + 1) + raw.slice(firstDot + 1).replace(/\./g, '');
    }
    const [intRaw = '', centsRaw] = normalized.split('.');
    const intStr = intRaw.replace(/^0+(?=\d)/, '');     // strip leading zeros
    const centsStr = centsRaw === undefined ? null : centsRaw.slice(0, 2);

    this.hasAmountCents = centsStr !== null && centsStr !== '';
    this.amountInput = intStr ? this.formatIntCommas(intStr) : (centsStr !== null ? '0' : '');

    if (centsStr !== null) {
      // Preserve the trailing dot/cents as the user types
      this.amountInput += '.' + centsStr;
    }

    const numeric = Number(intStr || '0') + Number('0.' + (centsStr ?? '0'));
    this.form.amount = numeric > 0 ? numeric : null;
  }
  private formatIntCommas(s: string): string {
    return s.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  }

  setQuickAmount(value: number): void {
    this.amountInput = this.formatIntCommas(String(value));
    this.hasAmountCents = false;
    this.form.amount = value;
  }

  get parsedAmount(): number {
    return this.form.amount ?? 0;
  }
  get amountCentsDisplay(): string {
    if (!this.hasAmountCents) return '.00';
    const cents = String(this.amountInput).split('.')[1] ?? '';
    return '.' + cents.padEnd(2, '0').slice(0, 2);
  }
  get amountHelperText(): string {
    if (this.parsedAmount > 0) return 'Looks good';
    return 'Type or pick a quick amount';
  }
  get amountHelperWarn(): boolean {
    return false;
  }

  // ===== Date shortcuts
  setDateShortcut(s: 'today' | 'yesterday'): void {
    const d = new Date();
    if (s === 'yesterday') d.setDate(d.getDate() - 1);
    this.form.date = d.toISOString().split('T')[0];
    this.dateShortcut = s;
  }
  openCustomDate(): void {
    this.dateShortcut = 'pick';
    setTimeout(() => this.datePickerEl?.nativeElement.showPicker?.(), 0);
  }
  onCustomDate(): void {
    this.dateShortcut = 'pick';
  }

  // ===== Form readiness
  get formReady(): boolean {
    return (this.form.amount ?? 0) > 0
        && this.form.description.trim().length > 0
        && !!this.form.category
        && !!this.form.date;
  }
  get missingPiecesText(): string {
    const missing: string[] = [];
    if (!((this.form.amount ?? 0) > 0))           missing.push('an amount');
    if (this.form.description.trim().length === 0) missing.push('a short note');
    return missing.join(' and ') || 'something';
  }

  // ===== Category icons
  categorySlug(name: string): string {
    const k = (name || '').toLowerCase();
    if (k.includes('food'))          return 'food';
    if (k.includes('transport'))     return 'transport';
    if (k.includes('housing'))       return 'housing';
    if (k.includes('utilit'))        return 'utilities';
    if (k.includes('health'))        return 'healthcare';
    if (k.includes('entertain'))     return 'entertainment';
    if (k.includes('shop'))          return 'shopping';
    if (k.includes('saving'))        return 'savings';
    if (k.includes('freelance'))     return 'freelance';
    if (k.includes('invest'))        return 'investments';
    if (k.includes('salary') || k.includes('income')) return 'income';
    return 'other';
  }
  categoryIcon(name: string): SafeHtml {
    const k = (name || '').toLowerCase();
    const exact = this.ICONS[k];
    if (exact) return this.sanitizer.bypassSecurityTrustHtml(exact);
    // Fuzzy fallback by keyword
    const fuzzyKey = Object.keys(this.ICONS).find(key => k.includes(key));
    return this.sanitizer.bypassSecurityTrustHtml(
      fuzzyKey ? this.ICONS[fuzzyKey] : this.ICONS['shopping']
    );
  }

  // ===== Submit
  onSubmit(): void {
    if (this.saving()) return;
    if (!this.formReady) return;

    this.errorMessage.set('');
    this.saving.set(true);

    this.api.createTransaction({
      description: this.form.description.trim(),
      amount: this.form.amount!,
      type: this.form.type,
      category: this.form.category,
      date: new Date(this.form.date).toISOString(),
      classification: this.form.type === 'expense' && this.form.classification !== ''
        ? this.form.classification
        : null,
    }).subscribe({
      next: () => {
        setTimeout(() => this.router.navigate(['/transactions']), 0);
      },
      error: (err) => {
        this.saving.set(false);
        const status = err?.status;
        if (status === 0)              this.errorMessage.set('Network error — check your connection.');
        else if (status === 401)       this.errorMessage.set('Your session expired. Please sign in again.');
        else if (status >= 500)        this.errorMessage.set('Server error. Try again in a moment.');
        else if (err?.error?.message)  this.errorMessage.set(err.error.message);
        else                           this.errorMessage.set('Could not save the transaction. Please try again.');
      },
    });
  }
}
