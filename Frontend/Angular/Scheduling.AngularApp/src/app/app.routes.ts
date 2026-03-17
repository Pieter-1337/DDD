import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: '/patients', pathMatch: 'full' },
  {
    path: 'patients',
    loadComponent: () =>
      import('./features/patients/patient-list/patient-list')
        .then(m => m.PatientList)
  },
  {
    path: 'patients/create',
    loadComponent: () =>
      import('./features/patients/create-patient/create-patient')
        .then(m => m.CreatePatient)
  },
  {
    path: 'patients/:id',
    loadComponent: () =>
      import('./features/patients/patient-detail/patient-detail')
        .then(m => m.PatientDetail)
  },];
