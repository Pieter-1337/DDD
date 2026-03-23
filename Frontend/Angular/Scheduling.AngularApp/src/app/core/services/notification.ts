import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private snackbar = inject(MatSnackBar);

  success(message: string): void {
    this.snackbar.open(message, 'Close', { duration: 3000, panelClass: 'snackbar-success' });
  }

  error(message: string): void {
    this.snackbar.open(message, 'Close', { duration: 5000, panelClass: 'snackbar-error' });
  }
}
