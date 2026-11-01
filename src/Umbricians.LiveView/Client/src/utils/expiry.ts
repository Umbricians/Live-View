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

/** "Fri 31 Oct, 23:59" in a time zone (the browser's own when none is given). */
export function formatAt(at: Date, timeZone?: string) {
  return new Intl.DateTimeFormat(undefined, {
    timeZone,
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  }).format(at);
}

/** "Denver time" for a configured zone, or "your time". */
export function timeZoneLabel(timeZone?: string) {
  return timeZone ? `${timeZone.split("/").pop()!.replace(/_/g, " ")} time` : "your time";
}

/** "3 days 4 h", "5 h 12 min" or "12 min". */
export function formatDuration(ms: number) {
  const minutes = Math.max(0, Math.round(ms / 60_000));
  const days = Math.floor(minutes / (60 * 24));
  const hours = Math.floor((minutes % (60 * 24)) / 60);
  const rest = minutes % 60;
  if (days > 0) return hours > 0 ? `${days} ${days === 1 ? "day" : "days"} ${hours} h` : `${days} ${days === 1 ? "day" : "days"}`;
  if (hours > 0) return rest > 0 ? `${hours} h ${rest} min` : `${hours} h`;
  return `${rest} min`;
}
