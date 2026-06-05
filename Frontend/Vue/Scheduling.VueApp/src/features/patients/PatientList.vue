<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import DataTable from 'primevue/datatable';
import Column from 'primevue/column';
import Button from 'primevue/button';
import Select from 'primevue/select';
import Tag from 'primevue/tag';
import ProgressSpinner from 'primevue/progressspinner';
import { usePatients } from '@core/composables/use-patients';
import { type Patient, PatientStatus } from '@core/models/patient';
const router = useRouter();

const selectedStatus = ref<string>('');
const statusOptions = [
    {label: 'All', value: ''},
    {label: 'Active', value: PatientStatus.Active},
    {label: 'Suspended',  value: PatientStatus.Suspended},
    {label: 'Deleted', value: PatientStatus.Deleted},
]


const {data: patients, isPending } = usePatients(selectedStatus);

function severityFor(status: PatientStatus) {
    return status === PatientStatus.Active ? 'success' : status === PatientStatus.Suspended ? 'warn' : 'danger';
}

/** Compile-checked column field name — typos against Patient become type errors. */
const col = <K extends keyof Patient>(key: K) => key;
</script>

<template>
    <h1 class="text-2xl font-bold mb-4">Patients</h1>
    <div class="flex items-center justify-between mb-4">
        <Select v-model="selectedStatus" :options="statusOptions" option-label="label" option-value="value" placeholder="Status" class="w-48" />
        <Button label="Create patient" icon="pi pi-plus" @click="router.push('/patients/create')" />
    </div>

    <div v-if="isPending" class="flex justify-center py-12">
        <ProgressSpinner />
    </div>

    <DataTable v-else :value="patients ?? []" stripedRows>
        <Column :field="col('firstName')" header="First Name"></Column>
        <Column :field="col('lastName')" header="Last Name"></Column>
        <Column :field="col('email')" header="Email"></Column>
        <Column  header="Status">
            <template #body="{ data }: { data: Patient }">
                <Tag :value="data.status" :severity="severityFor(data.status)" />
            </template>
        </Column>
        <Column header="Actions">
            <template #body="{ data }: { data: Patient }">
                <Button label="View" @click="router.push(`/patients/${data.id}`)" />
            </template>
        </Column>
    </DataTable>
</template>