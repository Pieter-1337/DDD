<script setup lang="ts">
import { useRouter } from 'vue-router';
import { useNotification } from '@core/composables/use-notifications';
import { useActivatePatient, useDeletePatient, usePatient, useSuspendPatient } from '@/core/composables/use-patients';
import { computed } from 'vue';
import { PatientStatus } from '@/core/models/patient';
import { Button, Card } from 'primevue';


const props = defineProps<{id: string}>();
const router = useRouter();
const notify = useNotification();

const {data: patient, isPending} = usePatient(props.id);

const isSuspended = computed(() => patient.value?.status === PatientStatus.Suspended);
const isDeleted = computed(() => patient.value?.status === PatientStatus.Deleted);

const suspend = useSuspendPatient();
const activate = useActivatePatient();
const remove = useDeletePatient();

function toggleStatus() {
    const mutation = isSuspended.value ? activate : suspend;
    mutation.mutate(props.id, {
        onSuccess:(res) => (res.success ? notify.success(res.message) : notify.error(res.message)),
        onError:(err) => notify.error(err.message)
    })
}

function deletePatient(){
    remove.mutate(props.id, {
        onSuccess:(res) => {
            if(res.success){
                notify.success(res.message);
                router.push('/patients');
                return;
            } 

            notify.error(res.message)
        },
        onError:(err) => notify.error(err.message)
    })
}

function formatDate(iso: string){
    return new Date(iso).toLocaleDateString();
}
</script>

<template>
    <div v-if="isPending" class="flex justify-center py-12">
        <ProgressSpinner />
    </div>

    <template v-else-if="patient">
        <div class="flex items-center gap-2 mb-4">
            <h1 class="text-2xl font-bold">{{ patient.firstName }} {{ patient.lastName }}</h1>
            <Button v-if="!isDeleted" icon="pi pi-trash" severity="danger" text rounded aria-label="Delete patient" :loading="remove.isPending.value" @click="deletePatient">
            </Button>
        </div>

        <Card>
            <template #content>
                <p><strong>Email: </strong>{{ patient.email }}</p>
                <p><strong>Status: </strong>{{ patient.status }}</p>
                <p><strong>Date of birth: </strong>{{ formatDate(patient.dateOfBirth) }}</p>
            </template>
            <template #footer>
                <div class="flex gap-2">
                    <Button
                        v-if="!isDeleted"
                        :label="isSuspended ? 'Activate':'Suspend'"
                        severity="warn"
                        :loading="suspend.isPending.value || activate.isPending.value"
                        @click="toggleStatus"
                    />
                     <Button label="Back to list" text @click="router.push('/patients')" />
                </div>
            </template>
        </Card>
    </template>

</template>