/// <reference types="vite/client" />

interface ImportMetaEnv {
    readonly SCHEDULING_API_URL: string;
    readonly BILLING_API_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
