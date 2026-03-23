import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { PatientApi } from '@core/services/patient-api';
import { CreatePatientRequest, CreatePatientResponse } from '@core/models/patient.model';
import { MatFormField, MatLabel, MatError, MatFormFieldModule } from "@angular/material/form-field";
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatInputModule } from '@angular/material/input';
import { HttpErrorResponse } from '@angular/common/http';
import { NotificationService } from '@core/services/notification';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-create-patient',
  standalone: true,
  imports: [MatFormField, MatLabel, MatError, ReactiveFormsModule, MatDatepickerModule, MatNativeDateModule, MatInputModule, MatButtonModule],
  templateUrl: './create-patient.html',
  styleUrl: './create-patient.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CreatePatient {
  private patientService = inject(PatientApi);
  private fb = inject(FormBuilder);
  private notification = inject(NotificationService);
  router = inject(Router);

  isSubmitting = signal(false);

  form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    dateOfBirth: [null as Date | null, Validators.required],
    status: ['Active']
  });

  submit(): void {
    if (this.form.invalid){
      this.form.markAllAsTouched();
      return;
    }

    const rawValue = this.form.getRawValue();
    const dob = rawValue.dateOfBirth!;

    const request: CreatePatientRequest = {
      firstName: rawValue.firstName,
      lastName: rawValue.lastName,
      email: rawValue.email,
      dateOfBirth: dob.toISOString().split('T')[0],
      status: rawValue.status
    };

    this.isSubmitting.set(true);
    this.patientService.create(request).subscribe({
      next: (response: CreatePatientResponse) => {
        if(response.success){
          this.notification.success(response.message);
          this.router.navigate(['/patients']);
        } else {
          this.notification.error(response.message);
          this.isSubmitting.set(false);
        }
      },
      error: (err: HttpErrorResponse) => {
        console.log("Failed to create patient", err)
        this.isSubmitting.set(false);
      }
    });
  }
}
