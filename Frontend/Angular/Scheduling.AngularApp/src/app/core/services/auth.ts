import { HttpClient } from "@angular/common/http";
import { computed, Inject, Injectable, signal } from "@angular/core";
import { AppRoles } from "@core/constants/approles";
import { UserInfo } from "@core/models/user-info.model";
import { catchError, Observable, of, tap } from "rxjs";
import { environment } from '../../../environments/environment';

/**
 * Authentication service using Angular signals for reactive state.
 *
 * This service does NOT manage tokens or implement OIDC flow.
 * The API handles all authentication. Angular just:
 * 1. Checks auth state via /auth/current-user
 * 2. Redirects to /auth/login for login
 * 3. Calls /auth/logout for logout
 */
@Injectable({providedIn: 'root' })
export class AuthService {
  // Private writable signals
  private currentUser = signal<UserInfo | null>(null);
  private loading = signal<boolean>(true);

  // Public readonly signals
  user = this.currentUser.asReadonly();
  isAuthenticated = computed(() => this.currentUser() !== null);
  isLoading = this.loading.asReadonly();

  // Computed role checks
  isAdmin = computed(() => this.currentUser()?.roles.includes(AppRoles.Admin) ?? false);
  isDoctor = computed(() => this.currentUser()?.roles.includes(AppRoles.Doctor) ?? false);

  /**
   *
   */
  constructor(private http: HttpClient) {}

  /**
   * Check authentication status by calling /auth/current-user
   * This is called on app initialization and after login
   *
   * The API returns user info if cookie is valid, 401 if not
   *
   * Note: If the user is not logged in, the API returns 401.
   * The auth interceptor catches this and redirects to login before
   * the catchError below runs — this is the forced login behavior.
   * The catchError still handles non-401 errors (network failures, 500s).
   */
  checkAuth(): Observable<UserInfo | null> {
    this.loading.set(true);

    return this.http.get<UserInfo>(`${environment.schedulingApiUrl}/auth/current-user`).pipe(
      tap(user => {
        this.currentUser.set(user);
        this.loading.set(false);
      }),
      catchError(() => {
        // Non-401 errors (network failures, 500s etc...)
        this.currentUser.set(null);
        this.loading.set(false);
        return of(null);
      })
    );
   }

   /**
   * Redirect to API's login endpoint.
   * The API handles the OIDC flow and redirects back to the app.
   */
   login(): void {
      const returnUrl = encodeURIComponent(window.location.origin);
      window.location.href = `${environment.schedulingApiUrl}/auth/login?returnUrl=${returnUrl}`;
   }

  /**
   * Log out by navigating to the API's logout endpoint.
   * This triggers a redirect chain: API clears cookie → IdentityServer logout → back to app.
   * Must be a full page navigation (not AJAX) because the OIDC logout flow involves redirects.
   */
   logout(): void {
      const returnUrl = encodeURIComponent(window.location.origin);
      window.location.href = `${environment.schedulingApiUrl}/auth/logout?returnUrl=${returnUrl}`;
   }


  /**
   * Check if user has a specific role
   */
  hasRole(role: string): boolean {
    return this.currentUser()?.roles.includes(role) ?? false;
  }
}
