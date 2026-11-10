import type { UmbExtensionManifestKind } from "@umbraco-cms/backoffice/extension-registry";
import { LIVE_VIEW_PANEL_TYPE } from "./constants.js";
import { LIVE_VIEW_PERMISSION_CONDITION_ALIAS, LIVE_VIEW_PERMISSION_VIEW } from "../permissions/constants.js";

// Sites use these in their own manifests; see docs/getting-started.md.
export const LIVE_VIEW_DASHBOARD_ALIAS = "Umbricians.LiveView.Dashboard";
export const LIVE_VIEW_DASHBOARD_KIND = "umbriciansLiveView";

const meta = {
  label: "#umbriciansLiveView_dashboardName",
  pathname: "live-view",
};

export const manifests: Array<UmbExtensionManifest | UmbExtensionManifestKind> = [
  {
    type: "kind",
    alias: "Umbricians.LiveView.Kind.Dashboard",
    matchType: "dashboard",
    matchKind: LIVE_VIEW_DASHBOARD_KIND,
    manifest: {
      type: "dashboard",
      js: () => import("./live-view-dashboard.element.js"),
      weight: -100,
      meta,
    },
  },
  {
    name: "Umbricians Live View Dashboard",
    alias: LIVE_VIEW_DASHBOARD_ALIAS,
    type: "dashboard",
    kind: LIVE_VIEW_DASHBOARD_KIND,
    meta,
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
