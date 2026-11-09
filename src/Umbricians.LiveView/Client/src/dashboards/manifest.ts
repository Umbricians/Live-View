import { LIVE_VIEW_PANEL_TYPE } from "./constants.js";
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
  {
    name: "Umbricians Live View Activity Panel",
    alias: "Umbricians.LiveView.Panel.Activity",
    type: LIVE_VIEW_PANEL_TYPE,
    element: () => import("./live-view-activity.element.js"),
    weight: 300,
  },
  {
    name: "Umbricians Live View Backoffice Users Panel",
    alias: "Umbricians.LiveView.Panel.Users",
    type: LIVE_VIEW_PANEL_TYPE,
    element: () => import("./live-view-users.element.js"),
    weight: 200,
  },
  {
    name: "Umbricians Live View Members Panel",
    alias: "Umbricians.LiveView.Panel.Members",
    type: LIVE_VIEW_PANEL_TYPE,
    element: () => import("./live-view-members.element.js"),
    weight: 100,
  },
];
