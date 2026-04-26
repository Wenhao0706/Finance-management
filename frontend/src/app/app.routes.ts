import { Routes } from '@angular/router';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { YearlyDashboardComponent } from './components/dashboard/yearly-dashboard.component';
import { TransactionsComponent } from './components/transactions/transactions.component';
import { TransactionFormComponent } from './components/transaction-form/transaction-form.component';
import { LoginComponent } from './components/login/login.component';
import { BudgetSettingsComponent } from './components/settings/budget-settings.component';
import { CategorySettingsComponent } from './components/settings/category-settings.component';
import { authGuard } from './guards/auth.guard';
import { guestGuard } from './guards/guest.guard';

export const routes: Routes = [
  { path: 'login', component: LoginComponent, canActivate: [guestGuard] },
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'dashboard', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'dashboard/month/:year/:month', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'dashboard/year/:year', component: YearlyDashboardComponent, canActivate: [authGuard] },
  { path: 'transactions', component: TransactionsComponent, canActivate: [authGuard] },
  { path: 'transactions/new', component: TransactionFormComponent, canActivate: [authGuard] },
  { path: 'settings/budget', component: BudgetSettingsComponent, canActivate: [authGuard] },
  { path: 'settings/categories', component: CategorySettingsComponent, canActivate: [authGuard] },
];
