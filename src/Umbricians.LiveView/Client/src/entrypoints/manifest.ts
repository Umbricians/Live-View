export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Umbricians Live View Entrypoint",
    alias: "Umbricians.LiveView.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },
];
