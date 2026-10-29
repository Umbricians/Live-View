export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Umbricians Live View Dashboard",
    alias: "Umbricians.LiveView.Dashboard",
    type: "dashboard",
    js: () => import("./live-view-dashboard.element.js"),
    weight: -100,
    meta: {
      label: "Live View",
      pathname: "live-view",
    },
    conditions: [
      {
        alias: "Umb.Condition.SectionAlias",
        match: "Umb.Section.Settings",
      },
    ],
  },
];
