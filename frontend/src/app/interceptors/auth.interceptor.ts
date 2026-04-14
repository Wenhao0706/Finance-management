import { inject } from '@angular/core';
import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpEvent } from '@angular/common/http';
import { Observable, from, switchMap, catchError } from 'rxjs';
import { AuthService } from '../services/auth.service';

// Attaches two headers to every outbound API request:
//   Authorization: Bearer <Firebase ID token>  — who the user is
//   X-Firebase-AppCheck: <App Check token>      — proof this call came from our real app
// Both tokens are fetched in parallel so the second call doesn't block on the first.
// If the App Check token can't be produced (offline, ad-blocker, etc.), the header
// is simply omitted — backend AppCheckMiddleware logs the miss and, in enforce mode,
// rejects with 401.
export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  const authService = inject(AuthService);

  return from(
    Promise.all([authService.getIdToken(), authService.getAppCheckToken()]),
  ).pipe(
    switchMap(([idToken, appCheckToken]) => {
      const headers: Record<string, string> = {};
      if (idToken) headers['Authorization'] = `Bearer ${idToken}`;
      if (appCheckToken) headers['X-Firebase-AppCheck'] = appCheckToken;

      const outbound = Object.keys(headers).length > 0 ? req.clone({ setHeaders: headers }) : req;
      return next(outbound);
    }),
    catchError((error) => {
      if (error.status === 401) {
        authService.logout();
      }
      throw error;
    }),
  );
};
