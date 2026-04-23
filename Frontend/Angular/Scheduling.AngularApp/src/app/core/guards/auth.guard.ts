// Frontend/Angular/Scheduling.AngularApp/src/app/core/guards/auth.guard.ts
import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth';

/**
 * Route guard that requires authentication
 * Redirects to login if user is not authenticated
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);

  if(authService.isAuthenticated()) {
    return true;
  }

  // Not authenticated - redirect to login
  authService.login();
  return false;
}

/**
 * Route guard that requires a specific role
 * Returns 403 if user doesn't have the required role
 */
export function roleGuard(role: string): CanActivateFn {
  return(route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if(!authService.isAuthenticated()) {
      // Not authenticated - redirect to login
      authService.login();
      return false;
    }

    if(!authService.hasRole(role)) {
      // User is authenticated but doesn't have the required role - redirect to forbidden page or show message
      router.navigate(['/forbidden']);
      return false;
    }

    return true;
  }
}
