import { html, nothing } from "@umbraco-cms/backoffice/external/lit";
import { formatAgo } from "./format.js";

/** One way someone is online, e.g. the website or MCP, and when they were last seen that way (null if they aren't). */
export interface OnlineVia {
  label: string;
  lastSeen: string | null;
}

/**
 * Tags for each way someone is online, each saying when they were last seen that way. Only shown when `show` is set:
 * while everyone online is on the site, the tags would say nothing.
 */
export function renderOnlineVia(show: boolean, ways: Array<OnlineVia>, now: number) {
  if (!show) return nothing;
  return ways
    .filter((way) => way.lastSeen !== null)
    .map(
      (way) =>
        html`<uui-tag look="secondary" title=${`${way.label} · ${formatAgo(now - Date.parse(way.lastSeen!))}`}
          >${way.label}</uui-tag
        >`,
    );
}
