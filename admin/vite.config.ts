/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173
  },
  test: {
    environment: 'jsdom',
    include: ['tests/**/*.spec.ts'],
    exclude: ['e2e/**', 'node_modules/**']
  }
})
