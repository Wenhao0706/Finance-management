import { Component, OnInit } from '@angular/core';
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

  form = {
    description: '',
    amount: null as number | null,
    type: 'expense' as 'income' | 'expense',
    category: '',
    date: new Date().toISOString().split('T')[0]
  };

  constructor(private api: ApiService, public router: Router) {}

  ngOnInit(): void {
    this.api.getCategories().subscribe(data => {
      this.categories = data;
      this.filterCategories();
    });
  }

  filterCategories(): void {
    this.filteredCategories = this.categories.filter(c => c.type === this.form.type);
    if (!this.filteredCategories.find(c => c.name === this.form.category)) {
      this.form.category = this.filteredCategories[0]?.name ?? '';
    }
  }

  onSubmit(): void {
    if (!this.form.description || !this.form.amount || !this.form.category) return;

    this.api.createTransaction({
      description: this.form.description,
      amount: this.form.amount,
      type: this.form.type,
      category: this.form.category,
      date: new Date(this.form.date).toISOString()
    }).subscribe(() => this.router.navigate(['/transactions']));
  }
}
