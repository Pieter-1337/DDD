import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { Patient } from '@core/models/patient.model';
import { PatientApi } from '@core/services/patient-api';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-patient-detail',
  standalone: true,
  imports: [MatProgressSpinnerModule, DatePipe, MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './patient-detail.html',
  styleUrl: './patient-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientDetail implements OnInit {
  private patientService = inject(PatientApi);
  private route = inject(ActivatedRoute);
  router = inject(Router);
  private snackbar = inject(MatSnackBar);

  patient = signal<Patient | null>(null);
  isSuspended = computed(() => this.patient()!.status === 'Suspended')
  isDeleted = computed(() => this.patient()!.status === 'Deleted')
  isLoading = signal<boolean>(false);

    ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.loadPatient(id);
  }

  private loadPatient(id: string): void{
    this.isLoading.set(true);
    this.patientService.getById(id).subscribe({
      next: (patient) => {
        this.patient.set(patient)
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    })
  }

  suspend(){
    const id = this.patient()!.id;
    this.patientService.suspend(id).subscribe({
      next: () => this.loadPatient(id)
    });
  }

  activate(){
    const id = this.patient()!.id;
    this.patientService.activate(id).subscribe({
      next: () => this.loadPatient(id)
    });
  }

  delete(){
    const id = this.patient()!.id;
    this.patientService.delete(id).subscribe({
      next: (response) => {
        if(response.success){
          this.snackbar.open(response.message, 'Close', { duration: 3000, panelClass: 'snackbar-success' });
          this.router.navigate(['/patients']);
        } else {
          this.snackbar.open(response.message, 'Close', { duration: 5000, panelClass: 'snackbar-error' });
        }
      },
      error: (err) => {
        console.log("Failed to delete patient", err);
      }
    });
  }
}
