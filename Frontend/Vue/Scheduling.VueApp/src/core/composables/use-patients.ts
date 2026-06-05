import { useQuery, useMutation, useQueryClient } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import { patientApi } from '@core/api/patient-api';
import type { CreatePatientRequest } from '@core/models/patient';

const patientKeys = {
    all: ['patients'] as const,
    lists: ['patients', 'list'] as const,
    list: (status?: string) => ['patients', 'list', status ?? 'all'] as const,
    detail: (_id: string) => ['patients', 'detail', 'id'] as const
}

/**
 * List patients, optionally filtered by status.
 * `status` may be a ref/getter — when it changes, the query refetches automatically.
 */
export function usePatients(status: MaybeRefOrGetter<string | undefined>) {
    const queryKey = computed(() => patientKeys.list(toValue(status) || undefined));

    return useQuery({
        queryKey,
        queryFn: () => patientApi.getAll({status: toValue(status) || undefined})
    });
}

/** Fetch a single patient by id. */
export function usePatient(id: MaybeRefOrGetter<string>) {
    const queryKey = computed(() => patientKeys.detail(toValue(id)));

    return useQuery({
        queryKey,
        queryFn: () => patientApi.getById(toValue(id))
    });
}

/** Create a patient, then invalidate the list so it refetches. */
export function useCreatePatient() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (request: CreatePatientRequest) => patientApi.create(request),
        onSuccess: () => queryClient.invalidateQueries( {queryKey: patientKeys.all })
    });
}

/** Suspend a patient, then invalidate both the detail and lists caches. */
export function useSuspendPatient() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (id: string) => patientApi.suspend(id),
        onSuccess: (_result, id) =>  {
            queryClient.invalidateQueries( {queryKey: patientKeys.detail(id) });
            queryClient.invalidateQueries( {queryKey: patientKeys.lists })
        }
    });
}

/** Activate a patient, then invalidate both the detail and lists caches. */
export function useActivatePatient() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (id: string) => patientApi.activate(id),
        onSuccess: (_result, id) => {
            queryClient.invalidateQueries( {queryKey: patientKeys.detail(id) });
            queryClient.invalidateQueries( {queryKey: patientKeys.lists })
        }
    });
}

/** Delete a patient, then invalidate the lists caches. */
export function useDeletePatient() {
    const queryClient = useQueryClient();
    return useMutation({
        mutationFn: (id: string) => patientApi.delete(id),
        onSuccess: () => {
            queryClient.invalidateQueries( {queryKey: patientKeys.lists })
        }
    })
}
