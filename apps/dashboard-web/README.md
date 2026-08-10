# dashboard-web

The Vue 3 + TypeScript platform dashboard — every module's UI renders into this shell (plan §11,
CLAUDE.md `apps/`).

- **State:** [Pinia](https://pinia.vuejs.org/) (`src/stores/`).
- **Routing:** [vue-router](https://router.vuejs.org/) (`src/router/`), with a global
  `beforeEach` guard reading each route's `meta.requiresAuth` (declared in
  `src/router/route-meta.d.ts`).
- **i18n:** [vue-i18n](https://vue-i18n.intlify.dev/) (`src/i18n/`), English-only for now
  (`src/i18n/locales/en.json`); add locales here as they're needed.
- **API client:** `src/lib/api-client.ts` — thin typed `fetch` wrapper that attaches the bearer
  token from the auth store and targets the gateway (`VITE_API_BASE_URL`, defaults to `/api`) so
  callers never hit a service URL directly. A `401` clears auth state and redirects to `/login`.
- **Auth boundary:** `src/stores/auth.ts` holds the access token and `isAuthenticated`.
  `login()`/`logout()` are real seams but there's no credential exchange yet — that arrives with
  the identity-workspace service (M00's control plane). Until then, the login view's "Continue"
  button calls `login()` with a placeholder token so the guard/API client wiring can be exercised
  end-to-end.

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
