import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from './services/auth.service';
import { map } from 'rxjs';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  title = 'Finance Management';

  isInitialized$ = this.authService.currentUser$.pipe(map((user) => user !== undefined));
  isLoggedIn$ = this.authService.currentUser$.pipe(map((user) => !!user));
  userName$ = this.authService.currentUser$.pipe(map((user) => user?.displayName || user?.email || 'User'));
  emailVerified$ = this.authService.currentUser$.pipe(map((user) => user?.emailVerified ?? true));

  constructor(private authService: AuthService) {}

  logout(): void {
    this.authService.logout();
  }
}
