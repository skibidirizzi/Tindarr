import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const uiPort = process.env.VITE_UI_PORT
  ? parseInt(process.env.VITE_UI_PORT, 10)
  : 5173

export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: uiPort,
    hmr: {
      host: 'localhost',
      clientPort: uiPort,
      protocol: 'ws',
    },
    proxy: {
      '/api': {
        target: process.env.VITE_PROXY_TARGET ?? 'http://localhost:5080',
        changeOrigin: true,
        secure: false,
      },
    },
  },
  preview: {
    host: true,
  },
})
