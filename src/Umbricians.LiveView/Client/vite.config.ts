import { defineConfig } from "vite";

export default defineConfig({
  build: {
    lib: {
      entry: {
        // Bundle registers one or more manifests
        "umbricians-live-view": "src/bundle.manifests.ts",
        // The `@umbricians/live-view` import map target. Kept apart from the bundle, because Umbraco registers every export of a bundle as an extension.
        "umbricians-live-view-api": "src/index.ts",
      },
      formats: ["es"],
      fileName: (_format, name) => `${name}.js`,
    },
    outDir: "../wwwroot/App_Plugins/UmbriciansLiveView", // your web component will be saved in this location
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      external: [/^@umbraco/],
    },
  },
});
