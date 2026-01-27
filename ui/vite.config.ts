import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // Dev convenience: let the UI call /api/v1/* on the Vite origin.
      // The dev script sets VITE_PROXY_TARGET; default is local API.
      "/api": {
        target: process.env.VITE_PROXY_TARGET ?? "http://localhost:5080",
        changeOrigin: true,
        secure: false
      }
    }
  }
});
