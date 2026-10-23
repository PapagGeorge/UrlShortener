/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';

// In development the Gateway is expected on localhost:8080 (run the stack on the Linux box and tunnel the port, or
// set API_TARGET to its address). In the container, nginx does the same job, see nginx.conf.template.
export default defineConfig(({ mode }) => {
  const apiTarget = loadEnv(mode, '.', '').API_TARGET || 'http://localhost:8080';
  return {
    plugins: [react()],
    server: {
      proxy: { '/api': { target: apiTarget, rewrite: (path: string) => path.replace(/^\/api/, '') } },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test-setup.ts'],
    },
  };
});
