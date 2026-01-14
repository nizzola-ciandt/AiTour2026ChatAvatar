import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(() => ({
  plugins: [react()],
  base: "/static/",  // Assets will be served from /static/ path
  build: {
    outDir: "dist",  // Build output goes to frontend/dist directory (will be copied to backend/static)
    emptyOutDir: true
  },
  server: {
    port: 5173,
    proxy: {
      // Proxy para endpoints da API (qualquer rota que não seja estática)
      "^/(sessions|ws|createuser|health)": {
        target: "http://localhost:8000",
        changeOrigin: true,
        ws: true  // Suporta WebSocket para /ws
      }
    }
  }
}));