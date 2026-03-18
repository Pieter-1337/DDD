import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { Patient } from '@core/models/patient.model';
import { PatientApi } from '@core/services/patient-api';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-patient-detail',
  imports: [MatProgressSpinnerModule, DatePipe, MatCardModule, MatButtonModule],
  templateUrl: './patient-detail.html',
  styleUrl: './patient-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientDetail implements OnInit {
  private patientService = inject(PatientApi);
  private route = inject(ActivatedRoute);
  router = inject(Router);

  patient = signal<Patient | null>(null);
  isSuspended = computed(() => this.patient()!.status === 'Suspended')
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
}
