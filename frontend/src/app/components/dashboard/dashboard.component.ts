import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { Summary } from '../../models/transaction.model';
import { Transaction } from '../../models/transaction.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  summary: Summary = { totalIncome: 0, totalExpense: 0, balance: 0 };
  recentTransactions: Transaction[] = [];

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.getSummary().subscribe(data => this.summary = data);
    this.api.getTransactions().subscribe(data => this.recentTransactions = data.slice(0, 5));
  }
}
