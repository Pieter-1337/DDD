<script setup lang="ts">
import { useRouter } from 'vue-router';
import { useCreatePatient } from '@core/composables/use-patients';
import { useNotification } from '@core/composables/use-notifications';
// + VeeValidate + Zod imports — see doc 04

const router = useRouter();
const notify = useNotification();
const create = useCreatePatient();
// form setup, validation schema, and submit handler — see doc 04
</script>