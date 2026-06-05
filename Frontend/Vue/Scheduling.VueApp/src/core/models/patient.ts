import type { SuccessOrFailureResponse } from '@shared/models/success-or-failure-response';

/**
 * Lifecycle status of a patient. Declared as a `const` object plus a derived
 * type so the names can be used as values (`PatientStatus.Suspended`) and as a
 * type (`status: PatientStatus`). The values are the wire strings, so API
 * responses assign without a cast.
 */
export const PatientStatus = {
  Active: 'Active',
  Suspended: 'Suspended',
  Deleted: 'Deleted',
} as const;

export type PatientStatus = (typeof PatientStatus)[keyof typeof PatientStatus];

/**
 * Patient entity returned from API
 */
export interface Patient {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // ISO 8601 date string
  status: PatientStatus;
}

/**
 * Request model for creating a new patient
 */
export interface CreatePatientRequest {
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // yyyy-MM-dd format,
  status: PatientStatus
}

/**
 * Response from CreatePatient command
 */
export interface CreatePatientResponse extends SuccessOrFailureResponse {
  patientId: string;
}

/**
 * Query parameters for filtering patients
 */
export interface PatientFilterParams {
  status?: string;
}