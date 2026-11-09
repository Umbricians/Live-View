import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import { LiveViewRequestError } from "../api/live-view.resource.js";

/** Rows per page in the online users and members tables. */
export const PAGE_SIZE = 25;

/** "Just now", "42 sec ago" or "3 min ago", worded in the user's language. */
export function formatAgo(localize: UmbLocalizationController, ms: number) {
  const seconds = Math.max(0, Math.round(ms / 1000));
  if (seconds < 10) return localize.term("umbriciansLiveView_justNow");
  if (seconds < 60) return localize.relativeTime(-seconds, "second", { style: "short" });
  return localize.relativeTime(-Math.round(seconds / 60), "minute", { style: "short" });
}

/** Why a request failed: the server's own explanation when it gave one, otherwise the status. */
export function describeError(localize: UmbLocalizationController, error: unknown) {
  if (error instanceof LiveViewRequestError && error.detail) return error.detail;
  return localize.term("umbriciansLiveView_requestFailed", error instanceof LiveViewRequestError ? error.status : undefined);
}
