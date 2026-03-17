import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatProgressSpinnerModule } from "@angular/material/progress-spinner";
import { DatePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-patient-detail',
  imports: [MatProgressSpinnerModule, DatePipe, MatCardModule],
  templateUrl: './patient-detail.html',
  styleUrl: './patient-detail.scss',
})
export class PatientDetail implements OnInit {
  private patientService = inject(patientApi);
  private route = inject(ActivatedRoute);
  router = inject(Router);


  patient = signal<Patient | null>(null);
  isLoading = signal<boolean>(false);

    ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.loadPatient(id);
  }

  private loadPatient(id: string): void{
    this.isLoading.set(true);
    this.patientService.Get(id).subscribe({
      next: (patient) => {
        this.patient.set(patient)
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    })
  }

  suspend(){
    const id = this.patient()!.id;
    this.patientService.Suspend(id).subscribe({
      next: () => this.loadPatient(id)
    });
  }
}
