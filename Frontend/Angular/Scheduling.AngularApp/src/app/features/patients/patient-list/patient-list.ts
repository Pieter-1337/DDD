import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router } from '@angular/router';
import { KeyValuePipe } from '@angular/common';
import { Patient } from '@core/models/patient.model';
import { PatientApi } from '@core/services/patient-api';

@Component({
  selector: 'app-patient-list',
  standalone: true,
  imports: [MatTableModule, MatButtonModule, MatSelectModule, MatFormFieldModule, MatProgressSpinnerModule, FormsModule, KeyValuePipe],
  templateUrl: './patient-list.html',
  styleUrl: './patient-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PatientList implements OnInit {
  private patientService = inject(PatientApi)
  router = inject(Router);

  patients = signal<Patient[]>([]);
  isLoading = signal<boolean>(true);
  selectedStatus = '';
  displayedColumns = ['firstName', 'lastName', 'email', 'status', 'actions'];
  statusOptions: Record<string, string> = {
    '' : 'All',
    'Active' : 'Active',
    'Suspended' :'Suspended',
    'Deleted' : 'Deleted'
  };

  ngOnInit(): void {
    this.loadPatients();
  }

  loadPatients() : void {
    this.isLoading.set(true);
    this.patientService.getAll({ status: this.selectedStatus || undefined }).subscribe({
      next: (patients) => {
        this.patients.set(patients);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }
}
