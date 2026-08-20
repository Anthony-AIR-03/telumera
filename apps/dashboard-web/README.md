# dashboard-web

The Vue 3 + TypeScript platform dashboard — every module's UI renders into this shell (plan §11,
CLAUDE.md `apps/`).

- **State:** [Pinia](https://pinia.vuejs.org/) (`src/stores/`).
- **Routing:** [vue-router](https://router.vuejs.org/) (`src/router/`), with a global
  `beforeEach` guard reading each route's `meta.requiresAuth` (declared in
  `src/router/route-meta.d.ts`).
- **i18n:** [vue-i18n](https://vue-i18n.intlify.dev/) (`src/i18n/`), English-only for now
  (`src/i18n/locales/en.json`); add locales here as they're needed.
- **API client:** `src/lib/api-client.ts` — thin typed `fetch` wrapper that attaches a bearer
  token (via the auth store's `getAccessToken()`) and targets the gateway (`VITE_API_BASE_URL`,
  defaults to `/api`, `http://localhost:5100` locally) so callers never hit a service URL
  directly. A `401` clears auth state and redirects to `/login`.
- **Auth boundary:** `src/stores/auth.ts` wraps a singleton MSAL `PublicClientApplication`
  (`src/lib/msal.ts`) for the `Telumera Dashboard` Entra app registration — a separate, Single-page
  application platform registration, distinct from the backend's `Telumera API` and the
  `Telumera CLI Test Client` used by `infrastructure/compose/scripts/get-dev-token.sh`. `login()`
  is an interactive popup sign-in; `getAccessToken()` tries a silent refresh first and only falls
  back to another popup when silent refresh genuinely requires interaction. `logout()` is
  local-only (`msalInstance.clearCache()`) — it does not call `logoutPopup()`, which performs a
  full front-channel logout at Azure AD's `end_session_endpoint` and, found by testing the real
  flow, opens a Microsoft popup asking the user to confirm ending their whole Entra SSO session
  (more than a dashboard "Log out" button should do, and it reads as an unrelated, confusing
  sign-in prompt). A future re-sign-in picks the still-live Microsoft session back up silently.
  The popup's redirect
  target is `auth-popup.html` (project root, a second Vite build entry — see `vite.config.ts`), a
  standalone static page that runs MSAL's `broadcastResponseToMainFrame()` and nothing else; it
  must stay separate from the SPA root or the app boots a second time inside the popup and races
  MSAL's own response handling (see the comment above `msalInstance` in `src/lib/msal.ts`). See
  `docs/runbooks/local-environment.md`'s "Auth" section for the app registration walkthrough and
  `apps/dashboard-web/.env.example` for the config values needed
  (`VITE_AZURE_AD_TENANT_ID`/`VITE_AZURE_AD_CLIENT_ID`/`VITE_AZURE_AD_API_SCOPE`).
- **Screens:** `DashboardView.vue` (workspace list/create) → `WorkspaceDetailView.vue` (a
  workspace's sites, members) → `SiteDetailView.vue` (a site's browser tokens — rotate/revoke —
  and module toggles). First usable pass; no response-composition endpoints exist on the gateway
  yet, so each view calls the relevant service's own endpoints directly through it.

## Project Setup

```sh
npm install
```

### Compile and Hot-Reload for Development

```sh
npm run dev
```

### Type-Check, Compile and Minify for Production

```sh
npm run build
```

### Lint with [ESLint](https://eslint.org/) and [oxlint](https://oxc.rs/docs/guide/usage/linter.html)

```sh
npm run lint
```

### Format with [Prettier](https://prettier.io/)

```sh
npm run format
```

## Recommended IDE Setup

[VS Code](https://code.visualstudio.com/) + [Vue (Official)](https://marketplace.visualstudio.com/items?itemName=Vue.volar) (and disable Vetur).
