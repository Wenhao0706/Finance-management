import { inject } from '@angular/core';
import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent } from '@angular/common/http';
import { Observable, from, switchMap, catchError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  const authService = inject(AuthService);

  return from(authService.getIdToken()).pipe(
    switchMap((token) => {
      if (token) {
        const cloned = req.clone({
          setHeaders: { Authorization: `Bearer ${token}` },
        });
        return next(cloned);
      }
      return next(req);
    }),
    catchError((error) => {
      if (error.status === 401) {
        authService.logout();
      }
      throw error;
    }),
  );
};
