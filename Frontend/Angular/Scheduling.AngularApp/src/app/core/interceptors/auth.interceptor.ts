import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, EMPTY, throwError } from 'rxjs';
import { AuthService } from '../services/auth';

/**
 * Auth interceptor that:
 * 1. Adds withCredentials: true to all requests (sends cookies)
 * 2. Handles 401 responses by redirecting to login
 * 3. Re-throws 403 responses for component-level handling
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);

  // Clone request with credentials and X-Requested-With header.
  // withCredentials tells the browser to send cookies with cross-origin requests.
  // X-Requested-With tells the API this is an AJAX call — the API returns 401
  // instead of redirecting to IdentityServer (which would fail via AJAX/CORS).
  const authReq = req.clone({
    withCredentials: true,
    setHeaders: { 'X-Requested-With': 'XMLHttpRequest' }
  });

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        // Redirect to login page
        authService.login();
        // Return EMPTY to cancel the original request observable
        return EMPTY;
      }

      // For 403 and other errors, re-throw the error for component-level handling
      return throwError(() => error);
    }));

}
