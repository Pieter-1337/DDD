import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { CreatePatientRequest, CreatePatientResponse, Patient, PatientFilterParams } from '@core/models/patient.model';
import { environment } from '@env/environment';
import { SuccessOrFailureResponse } from '@shared/models/success-or-failure-response.model';
import { Observable } from 'rxjs';

/**
 * Service for managing patient data via Scheduling.WebApi
 */
@Injectable({
  providedIn: 'root',
})
export class PatientApi {
  private http = inject(HttpClient);
  private baseUrl = `${environment.schedulingApiUrl}/api/patients`;

   /**
   * Get all patients with optional filtering
   * @param params Optional filter parameters (e.g., status)
   * @returns Observable of patient array
   */
  getAll(params?: PatientFilterParams): Observable<Patient[]> {
    let httpParams = new HttpParams();

    if(params?.status){
      httpParams = httpParams.set('status', params.status);
    }

    return this.http.get<Patient[]>(this.baseUrl, {params : httpParams});
  }

    /**
   * Get a single patient by ID
   * @param id Patient ID (GUID)
   * @returns Observable of patient
   */
  getById(id: string): Observable<Patient> {
    return this.http.get<Patient>(`${this.baseUrl}/${id}`);
  }

  /**
   * Create a new patient
   * @param request Patient creation data
   * @returns Observable of creation response
   */
  create(request: CreatePatientRequest): Observable<CreatePatientResponse> {
    return this.http.post<CreatePatientResponse>(this.baseUrl, request);
  }

   /**
   * Suspend a patient (change status to Suspended)
   * @param id Patient ID
   * @returns Observable of success/failure response
   */
  suspend(id: string): Observable<SuccessOrFailureResponse> {
    return this.http.post<SuccessOrFailureResponse>(`${this.baseUrl}/${id}/suspend`, null);
  }

  /**
   * Activate a patient (change status to Active)
   * @param id Patient ID
   * @returns Observable of success/failure response
   */
  activate(id: string): Observable<SuccessOrFailureResponse> {
    return this.http.post<SuccessOrFailureResponse>(`${this.baseUrl}/${id}/activate`, null);
  }

  /**
   * Delete a patient (soft delete)
   * @param id Patient ID
   * @returns Observable of success/failure response
   */
  delete(id: string): Observable<SuccessOrFailureResponse> {
    return this.http.delete<SuccessOrFailureResponse>(`${this.baseUrl}/${id}`);
  }
}
