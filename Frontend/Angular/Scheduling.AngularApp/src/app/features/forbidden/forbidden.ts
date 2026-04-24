import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './forbidden.html',
  styleUrl: './forbidden.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Forbidden {
  private router = inject(Router);

  goBack(): void {
    this.router.navigate(['/patients']);
  }
}
