import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { AuthService } from './services/auth.service';
import { filter, map } from 'rxjs';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  title = 'Finance Management';

  private authService = inject(AuthService);
  private router = inject(Router);

  isInitialized$ = this.authService.currentUser$.pipe(map((user) => user !== undefined));
  isLoggedIn$ = this.authService.currentUser$.pipe(map((user) => !!user));
  userName$ = this.authService.currentUser$.pipe(map((user) => user?.displayName || user?.email || 'User'));
  emailVerified$ = this.authService.currentUser$.pipe(map((user) => user?.emailVerified ?? true));

  // Mobile drawer state — `false` = closed (default). Signal so template
  // updates synchronously without manual change-detection ticks.
  sidebarOpen = signal(false);

  constructor() {
    // Auto-close the drawer whenever the route changes — typical mobile UX
    // expectation: tap a nav link, drawer dismisses.
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.sidebarOpen.set(false));
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((v) => !v);
  }

  closeSidebar(): void {
    this.sidebarOpen.set(false);
  }

  logout(): void {
    this.authService.logout();
  }
}
