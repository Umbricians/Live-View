import { LIVE_VIEW_PERMISSION_CONDITION_ALIAS, LIVE_VIEW_PERMISSION_VIEW } from "../permissions/constants.js";

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
      {
        alias: LIVE_VIEW_PERMISSION_CONDITION_ALIAS,
        permission: LIVE_VIEW_PERMISSION_VIEW,
      },
    ],
  },
];
