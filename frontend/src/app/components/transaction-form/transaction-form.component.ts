import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { Category } from '../../models/category.model';

@Component({
  selector: 'app-transaction-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './transaction-form.component.html',
  styleUrl: './transaction-form.component.scss'
})
export class TransactionFormComponent implements OnInit {
  categories: Category[] = [];
  filteredCategories: Category[] = [];

  // Three independent loading concerns — keeping them separate so the UI
  // can disable specific controls (e.g. submit button stays disabled until
  // categories load AND amount is valid AND submit isn't already in flight).
  loadingCategories = signal(true);
  saving = signal(false);
  errorMessage = signal('');

  form = {
    description: '',
    amount: null as number | null,
    type: 'expense' as 'income' | 'expense',
    category: '',
    date: new Date().toISOString().split('T')[0],
    classification: '' as 'Need' | 'Want' | 'Savings' | ''
  };

  constructor(private api: ApiService, public router: Router) {}

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

  filterCategories(): void {
    this.filteredCategories = this.categories.filter(c => c.type === this.form.type);
    if (!this.filteredCategories.find(c => c.name === this.form.category)) {
      this.form.category = this.filteredCategories[0]?.name ?? '';
    }
  }

  onSubmit(): void {
    if (this.saving()) return;          // hard double-submit guard
    if (!this.form.description || !this.form.amount || !this.form.category) return;

    this.errorMessage.set('');
    this.saving.set(true);

    this.api.createTransaction({
      description: this.form.description.trim(),
      amount: this.form.amount,
      type: this.form.type,
      category: this.form.category,
      date: new Date(this.form.date).toISOString(),
      classification: this.form.type === 'expense' && this.form.classification !== ''
        ? this.form.classification
        : null,
    }).subscribe({
      next: () => {
        // Defer navigation a tick so any focused element (Enter-on-button)
        // doesn't fire a stray click on the destination page.
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
