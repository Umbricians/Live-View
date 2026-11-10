/** The refresh choices a dashboard offers: the intervals in seconds and the one used until someone picks another. */
export interface RefreshIntervalSettings {
  refreshIntervals: ReadonlyArray<number>;
  defaultRefreshInterval: number;
}

/** The refresh intervals the dashboard offers when the site's settings can't be read, in seconds. */
export const INTERVALS = [2, 5, 10, 15, 30];
export const DEFAULT_INTERVAL = 5;

/** The package defaults, used only if the site's settings can't be read. */
export const BUILT_IN_REFRESH_SETTINGS: RefreshIntervalSettings = {
  refreshIntervals: INTERVALS,
  defaultRefreshInterval: DEFAULT_INTERVAL,
};

/**
 * The offered interval a stored or chosen value stands for; anything else (nothing stored, a value no longer offered)
 * is the default. The choices and default come from the site's dashboard settings, the built-in ones when not given.
 */
export function toInterval(value: unknown, settings: RefreshIntervalSettings = BUILT_IN_REFRESH_SETTINGS): number {
  const seconds = value === null || value === "" ? Number.NaN : Number(value);
  return settings.refreshIntervals.includes(seconds) ? seconds : settings.defaultRefreshInterval;
}
