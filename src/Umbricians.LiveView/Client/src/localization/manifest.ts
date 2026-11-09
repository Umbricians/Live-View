export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "localization",
    alias: "Umbricians.LiveView.Localization.En",
    name: "Umbricians Live View English",
    meta: { culture: "en" },
    js: () => import("./en.js"),
  },
];
