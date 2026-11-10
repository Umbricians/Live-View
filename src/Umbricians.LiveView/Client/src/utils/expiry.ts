/** The longest a member message can stay relevant, matching the server. */
export const LONGEST_MINUTES = 60 * 24 * 30;

/** The wall-clock time an instant shows in a time zone, read as if it were UTC. */
function wallClock(instant: number, timeZone: string | undefined) {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone,
    hourCycle: "h23",
    year: "numeric",
    month: "numeric",
    day: "numeric",
    hour: "numeric",
    minute: "numeric",
    second: "numeric",
  }).formatToParts(instant);
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)!.value);
  return Date.UTC(part("year"), part("month") - 1, part("day"), part("hour"), part("minute"), part("second"));
}

/** The next midnight in a time zone (the browser's own when none is given). */
export function nextMidnight(now: Date, timeZone?: string): Date {
  const today = new Date(wallClock(now.getTime(), timeZone));
  const midnight = Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() + 1);
  // Shift the wall-clock midnight by the zone's offset, then again in case that crossed a daylight saving change.
  let instant = midnight - (wallClock(midnight, timeZone) - midnight);
  instant = midnight - (wallClock(instant, timeZone) - instant);
  return new Date(instant);
}

/** "Denver" for a configured zone such as "America/Denver"; undefined when there is none. */
export function timeZoneCity(timeZone?: string) {
  return timeZone ? timeZone.split("/").pop()!.replace(/_/g, " ") : undefined;
}

/** A duration split into its two largest units: days and hours, hours and minutes, or minutes alone. Never negative. */
export function durationParts(ms: number): Array<{ value: number; unit: "day" | "hour" | "minute" }> {
  const minutes = Math.max(0, Math.round(ms / 60_000));
  const days = Math.floor(minutes / (60 * 24));
  const hours = Math.floor((minutes % (60 * 24)) / 60);
  const rest = minutes % 60;
  if (days > 0) return hours > 0 ? [{ value: days, unit: "day" }, { value: hours, unit: "hour" }] : [{ value: days, unit: "day" }];
  if (hours > 0) return rest > 0 ? [{ value: hours, unit: "hour" }, { value: rest, unit: "minute" }] : [{ value: hours, unit: "hour" }];
  return [{ value: rest, unit: "minute" }];
}
