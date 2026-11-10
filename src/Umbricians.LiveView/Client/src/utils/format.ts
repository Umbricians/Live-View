import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";

/** Rows per page in the online users and members tables. */
export const PAGE_SIZE = 25;

/** "Just now", "42s ago" or "3 min ago", in the user's language. */
export function formatAgo(localize: UmbLocalizationController, ms: number) {
  const seconds = Math.max(0, Math.round(ms / 1000));
  if (seconds < 10) return localize.term("umbriciansLiveView_justNow");
  if (seconds < 60) return localize.term("umbriciansLiveView_secondsAgo", seconds);
  return localize.term("umbriciansLiveView_minutesAgo", Math.round(seconds / 60));
}
