import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { UmbriciansLiveViewDashboardContext } from "./live-view-dashboard.context.js";

/** The Live View dashboard's context: panels read the refresh interval and paused state from it. */
export const UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT = new UmbContextToken<UmbriciansLiveViewDashboardContext>(
  "Umbricians.LiveView.Context.Dashboard",
);
