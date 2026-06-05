<script setup lang="ts">
import { Toast, Menubar } from 'primevue';
import { useRouter } from 'vue-router';

const router = useRouter();
const items = [
  {label: 'Patients', icon: 'pi pi-users', command: () => router.push('/patients')}
]

</script>


<template>
  <Toast />
  <Menubar :model="items" class="mb-4">
    <template #start>
      <span class="font-bold text-primary px-2">🏥 Patient Management</span>
    </template>
  </Menubar>

  <main class="px-6">
      <router-view />
  </main>
</template>

<style scoped></style>
