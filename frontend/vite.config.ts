import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react(), {
    name: 'khospace-deployment-metadata',
    generateBundle() {
      if (['frontend-only', 'cloud-connected'].includes(process.env.VITE_KHOSPACE_MODE ?? '')) {
        if (!/^[a-f0-9]{40}$/.test(process.env.VITE_KHOSPACE_REVISION ?? '')) throw Error('A source revision is required for a web export');
        this.emitFile({ type: 'asset', fileName: 'khospace-deployment.json',
          source: JSON.stringify({ mode: process.env.VITE_KHOSPACE_MODE, sourceRevision: process.env.VITE_KHOSPACE_REVISION }) });
      }
    },
  }],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/health': 'http://127.0.0.1:5080', '/api': 'http://127.0.0.1:5080' },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    coverage: { provider: 'v8', include: ['src/**/*.tsx', 'src/**/*.ts'], exclude: ['src/**/*.test.*', 'src/main.tsx', 'src/test-setup.ts'], thresholds: { lines: 80, functions: 80, statements: 80, branches: 80 } },
  },
});
