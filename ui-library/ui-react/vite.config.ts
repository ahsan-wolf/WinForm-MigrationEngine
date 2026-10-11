import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Library mode: ESM bundle + one stylesheet (tokens inlined). React stays a peer dependency.
export default defineConfig({
  plugins: [react()],
  build: {
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: () => 'index.js', cssFileName: 'styles' },
    rollupOptions: { external: ['react', 'react-dom', 'react/jsx-runtime'] },
    cssCodeSplit: false,
  },
});
