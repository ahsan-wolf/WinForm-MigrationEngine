import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// `npm run dev` proxies /api to the host (dotnet run in ../host, port 5280); the production build is served by the host itself.
export default defineConfig({
  base: './',
  plugins: [react()],
  resolve: { dedupe: ['react', 'react-dom'] },
  server: { proxy: { '/api': { target: 'http://127.0.0.1:5280', changeOrigin: false } } },
});
