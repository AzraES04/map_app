import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // ---- Test (Vitest) ----
  //
  // Vitest, Vite'in kendi donusum boru hattini kullaniyor: JSX, import
  // yollari ve eklentiler icin ayri bir yapilandirma (Jest/Babel) gerekmiyor.
  // Ayri bir dosya (vitest.config.js) da acilabilirdi ama o zaman iki dosya
  // birbirinden ayri dusebilirdi — aliaslar burada, testte orada.
  test: {
    // jsdom: bilesen testleri icin tarayici benzeri bir DOM.
    // Node'un kendisinde document/window yok.
    environment: 'jsdom',

    // Her test dosyasindan ONCE calisan hazirlik (jest-dom eslestiricileri).
    setupFiles: './src/test/kurulum.js',

    // describe/it/expect'i her dosyada import etmeye gerek kalmasin.
    globals: true,

    // Vitest varsayilan olarak node_modules'u da tarardi.
    include: ['src/**/*.test.{js,jsx}'],
  },

  server: {
    port: 5173,
    proxy: {
      // /api ile başlayan istekleri .NET backend'ine yönlendir
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },

      // Ödev 19: SignalR kanalı. `ws: true` ŞART — bu yol WebSocket'e
      // yükseltiliyor ve vekil bunu bilmezse el sıkışma 400 ile düşer.
      // (Yükseltme başarısız olsaydı SignalR sessizce long-polling'e
      // düşerdi; çalışır ama her mesaj için yeni HTTP isteği demek.)
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
