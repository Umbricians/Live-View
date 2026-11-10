import type { LiveViewSnapshot } from "../api/live-view.resource.js";

/** How many readings the chart keeps. */
export const MAX_READINGS = 120;

/** The readings with `reading` added, dropping the oldest beyond `max`. Never changes `readings` in place. */
export function addReading(
  readings: ReadonlyArray<LiveViewSnapshot>,
  reading: LiveViewSnapshot,
  max = MAX_READINGS,
): ReadonlyArray<LiveViewSnapshot> {
  return [...readings, reading].slice(-max);
}

/**
 * The readings after `last`, matched by identity since timestamps can repeat within a second; all of them when
 * `last` is undefined or no longer kept.
 */
export function readingsAfter(
  readings: ReadonlyArray<LiveViewSnapshot>,
  last: LiveViewSnapshot | undefined,
): ReadonlyArray<LiveViewSnapshot> {
  const index = last === undefined ? -1 : readings.indexOf(last);
  return readings.slice(index + 1);
}
