import { resolve } from 'node:path'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  build: {
    lib: {
      entry: resolve(import.meta.dirname, 'src/index.ts'),
      name: 'telumera',
      formats: ['es', 'iife'],
      fileName: (format) => (format === 'es' ? 'browser-sdk.mjs' : 'browser-sdk.global.js'),
    },
    sourcemap: true,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
  },
})
