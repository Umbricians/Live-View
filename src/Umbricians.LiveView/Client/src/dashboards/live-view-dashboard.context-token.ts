import { UmbContextToken, type UmbContextMinimal } from "@umbraco-cms/backoffice/context-api";
import type { Observable } from "@umbraco-cms/backoffice/observable-api";

/** What the Live View dashboard shares with the panels on it. */
export interface UmbriciansLiveViewDashboardContext extends UmbContextMinimal {
  /** The refresh interval in seconds. */
  readonly interval: Observable<number>;
  /** Whether the dashboard is paused: panels should not refresh while it is. */
  readonly paused: Observable<boolean>;
}

/** The Live View dashboard's context: panels read the refresh interval and paused state from it. */
export const UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT = new UmbContextToken<UmbriciansLiveViewDashboardContext>(
  "Umbricians.LiveView.Context.Dashboard",
);
