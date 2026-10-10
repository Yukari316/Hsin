import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  server: { proxy: { '/api': 'http://localhost:24515', '/assets': 'http://localhost:24515' } },
  test: { environment: 'jsdom', exclude: ['e2e/**', 'node_modules/**'] },
})
