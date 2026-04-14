import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { filter, map, take } from 'rxjs';
import { AuthService } from '../services/auth.service';

// Inverse of authGuard: keeps signed-in users OUT of auth-only routes
// (/login, signup view, forgot-password view). Without this, a user who's
// already signed in from another tab could navigate to /login and see the
// authenticated shell briefly overlap with the login layout.
export const guestGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.currentUser$.pipe(
    filter((user) => user !== undefined), // wait for Firebase bootstrap
    take(1),
    map<unknown, boolean | UrlTree>((user) => (user ? router.createUrlTree(['/dashboard']) : true)),
  );
};
