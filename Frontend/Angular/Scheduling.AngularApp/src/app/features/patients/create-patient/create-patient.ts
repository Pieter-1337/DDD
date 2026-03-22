import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { PatientApi } from '@core/services/patient-api';
import { CreatePatientRequest } from '@core/models/patient.model';
import { MatFormField, MatLabel, MatError, MatFormFieldModule } from "@angular/material/form-field";
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatInputModule } from '@angular/material/input';

@Component({
  selector: 'app-create-patient',
  standalone: true,
  imports: [MatFormField, MatLabel, MatError, ReactiveFormsModule, MatDatepickerModule, MatNativeDateModule, MatInputModule],
  templateUrl: './create-patient.html',
  styleUrl: './create-patient.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CreatePatient {
  private patientService = inject(PatientApi);
  private fb = inject(FormBuilder);
  router = inject(Router);

  isSubmitting = signal(false);

  form = this.fb.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    dateOfBirth: [null as Date | null, Validators.required]
  });

  submit(): void {
    if (this.form.invalid){
      this.form.markAllAsTouched();
      return;
    }

    const rawValue = this.form.getRawValue();
    const dob = rawValue.dateOfBirth!;

    const request: CreatePatientRequest = {
      firstName: rawValue.firstName!,
      lastName: rawValue.lastName!,
      email: rawValue.email!,
      dateOfBirth: dob.toISOString().split('T')[0],
      status: 'Active'
    };

    this.isSubmitting.set(true);
    this.patientService.create(request).subscribe({
      next: () => this.router.navigate(['/patients']),
      error: () => this.isSubmitting.set(false),
    });
  }
}
