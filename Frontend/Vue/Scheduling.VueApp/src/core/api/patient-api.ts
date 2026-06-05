import type { SuccessOrFailureResponse } from "@/shared/models/success-or-failure-response";
import type { CreatePatientRequest, CreatePatientResponse, Patient, PatientFilterParams } from "@core/models/patient";

const baseUrl = `${import.meta.env.VITE_SCHEDULING_API_URL}/api/patients`;

async function json<T>(response: Response): Promise<T> {
    if(!response.ok){
        throw new Error(`Request failed: ${response.status} ${response.statusText}`);
    }

    return response.json() as Promise<T>;
}

export const patientApi = {
    getAll(params?: PatientFilterParams): Promise<Patient[]> {
        const query = params?.status ? `?status=${encodeURIComponent(params.status)}` : '';
        return fetch(`${baseUrl}/${query}`).then(json<Patient[]>);
    },

    getById(id: string): Promise<Patient> {
        return fetch(`${baseUrl}/${id}`).then(json<Patient>);
    },

    create(request: CreatePatientRequest): Promise<CreatePatientResponse> {
        return fetch(baseUrl, {
            method: 'POST',
            headers: {'Content-Type': 'application/json' },
            body: JSON.stringify(request),
        }).then(json<CreatePatientResponse>);
    },

    suspend(id: string): Promise<SuccessOrFailureResponse> {
        return fetch(`${baseUrl}/${id}/suspend`, {
            method: 'POST'
        }).then(json<SuccessOrFailureResponse>)
    },

    activate(id: string): Promise<SuccessOrFailureResponse> {
        return fetch(`${baseUrl}/${id}/activate`, {
            method: 'POST'
        }).then(json<SuccessOrFailureResponse>)
    },

    delete(id: string): Promise<SuccessOrFailureResponse> {
        return fetch(`${baseUrl}/${id}/delete`, {
            method: 'DELETE'
        }).then(json<SuccessOrFailureResponse>)
    }
}