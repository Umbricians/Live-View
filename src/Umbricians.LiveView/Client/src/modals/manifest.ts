export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Umbricians Live View Send Message Modal",
    alias: "Umbricians.LiveView.Modal.SendMessage",
    type: "modal",
    element: () => import("./send-message-modal.element.js"),
  },
];
