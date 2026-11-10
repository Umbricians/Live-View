import { html, nothing } from "@umbraco-cms/backoffice/external/lit";
import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import { formatAgo } from "./format.js";

/** One way someone is online, e.g. the website or MCP, and when they were last seen that way (null if they aren't). */
export interface OnlineVia {
  /** The way's name, already in the user's language. */
  label: string;
  lastSeen: string | null;
}

/**
 * Tags for each way someone is online, each saying when they were last seen that way. Only shown when `show` is set:
 * while everyone online is on the site, the tags would say nothing.
 */
export function renderOnlineVia(
  localize: UmbLocalizationController,
  show: boolean,
  ways: Array<OnlineVia>,
  now: number,
) {
  if (!show) return nothing;
  return ways
    .filter((way) => way.lastSeen !== null)
    .map(
      (way) =>
        html`<uui-tag look="secondary" title=${`${way.label} · ${formatAgo(localize, now - Date.parse(way.lastSeen!))}`}
          >${way.label}</uui-tag
        >`,
    );
}
