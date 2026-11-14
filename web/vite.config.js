import { defineConfig } from "vite";
import { svelte } from "@sveltejs/vite-plugin-svelte";
import tailwindcss from "@tailwindcss/vite";

// Dev loop: vite proxies everything non-HTML to the API (dotnet run on :8080).
export default defineConfig({
  plugins: [svelte(), tailwindcss()],
  server: {
    proxy: {
      "/api": "http://localhost:8080",
      "/b": "http://localhost:8080",
      "/healthz": "http://localhost:8080",
    },
  },
  build: { outDir: "dist" },
});
