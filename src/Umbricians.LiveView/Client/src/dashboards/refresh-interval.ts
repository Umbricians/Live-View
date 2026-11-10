/** The refresh intervals the dashboard offers, in seconds. */
export const INTERVALS = [2, 5, 10, 15, 30];
export const DEFAULT_INTERVAL = 5;

/** The offered interval a stored or chosen value stands for; anything else (nothing stored, a value not offered) is the default. */
export function toInterval(value: unknown): number {
  const seconds = Number(value);
  return INTERVALS.includes(seconds) ? seconds : DEFAULT_INTERVAL;
}
