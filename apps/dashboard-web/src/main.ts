import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import { i18n } from './i18n'
import { ensureMsalInitialized } from './lib/msal'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(i18n)

// Eager, not lazy-on-click: loginPopup()'s window.open() must stay inside the same
// synchronous tick as the user's click, or Chrome silently blocks the popup. Awaiting
// MSAL's real (first-time) initialize() from inside a click handler breaks that. Doing it
// here means it's already resolved by the time anyone clicks "Sign in" — see lib/msal.ts.
await ensureMsalInitialized()

app.mount('#app')
