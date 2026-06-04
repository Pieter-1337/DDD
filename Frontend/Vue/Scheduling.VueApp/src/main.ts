import { createApp } from 'vue';
import PrimeVue from 'primevue/config';
import Aura from '@primeuix/themes/aura';
import ToastService from 'primevue/toastservice';
import App from './App.vue';
import router from './router';
import './style.css';
import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query';

const app = createApp(App)
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000, // Treat data as fresh for 30s before background refetch
      retry: 1,
    },
  },
});

app.use(router);
app.use(PrimeVue, {
    theme: {
    preset: Aura,
    options: {
      // Wrap PrimeVue styles in a CSS layer so Tailwind utilities can override
      // them predictably. Order matters — see Step 4.
      cssLayer: {
        name: 'primevue',
        order: 'theme, base, primevue',
      },
    },
  },
});
app.use(VueQueryPlugin, { queryClient });
app.use(ToastService);
app.mount('#app')
