import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    rollupOptions: {
      // auth-popup.html (MSAL's popup redirect target, src/lib/msal.ts) is a second,
      // standalone HTML entry — not linked from index.html — so it needs to be listed
      // explicitly or the production build won't emit it.
      input: {
        main: fileURLToPath(new URL('./index.html', import.meta.url)),
        authPopup: fileURLToPath(new URL('./auth-popup.html', import.meta.url)),
      },
    },
  },
})
