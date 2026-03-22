/**
 * Patient entity returned from API
 */
export interface Patient {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // ISO 8601 date string
  status: string;       // "Active" | "Suspended"
}

/**
 * Request model for creating a new patient
 */
export interface CreatePatientRequest {
  firstName: string;
  lastName: string;
  email: string;
  dateOfBirth: string;  // yyyy-MM-dd format,
  status: string
}

/**
 * Response from CreatePatient command
 */
export interface CreatePatientResponse {
  success: boolean;
  patientId: string;
  errors?: string[];
}

/**
 * Query parameters for filtering patients
 */
export interface PatientFilterParams {
  status?: string;
}
