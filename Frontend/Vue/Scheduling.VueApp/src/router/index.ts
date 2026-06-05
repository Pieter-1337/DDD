import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
  {path: '/', redirect: '/patients'},
  {path: '/patients', name: 'patient-list', component: () => import('@features/patients/PatientList.vue')},
  {path: '/patients/create', name: 'create-patient', component: () => import('@features/patients/CreatePatient.vue')},
  {path: '/patients/:id', name: 'patient-detail', component: () => import('@features/patients/PatientDetail.vue'), props: true}
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})

export default router
