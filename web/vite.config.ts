import react from '@vitejs/plugin-react'
import tailwind from '@tailwindcss/vite'
import { defineConfig } from 'vite'

export default defineConfig({
  base: './',
  build: { target: 'chrome120', assetsInlineLimit: 0, license: { fileName: "licenses/frontend.md" } },
  plugins: [
    react(),
    tailwind(),
    {
      name: 'winlayer-local-content-policy',
      apply: 'build',
      transformIndexHtml: () => [{
        tag: 'meta',
        injectTo: 'head-prepend',
        attrs: {
          'http-equiv': 'Content-Security-Policy',
          content: "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data:; base-uri 'none'; form-action 'none'; object-src 'none'; frame-src 'none'",
        },
      }],
    },
  ],
})
