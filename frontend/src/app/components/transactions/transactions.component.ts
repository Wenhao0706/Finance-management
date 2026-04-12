import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { Transaction } from '../../models/transaction.model';

@Component({
  selector: 'app-transactions',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './transactions.component.html',
  styleUrl: './transactions.component.scss'
})
export class TransactionsComponent implements OnInit {
  transactions: Transaction[] = [];

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadTransactions();
  }

  loadTransactions(): void {
    this.api.getTransactions().subscribe(data => this.transactions = data);
  }

  deleteTransaction(id: number): void {
    if (confirm('Are you sure you want to delete this transaction?')) {
      this.api.deleteTransaction(id).subscribe(() => this.loadTransactions());
    }
  }
}
