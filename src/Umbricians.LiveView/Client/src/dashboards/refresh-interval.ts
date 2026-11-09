/** The interval in `value` if it is still one of the choices, otherwise the fallback, e.g. for a remembered choice the site no longer offers. */
export function offeredInterval(value: string | null, choices: ReadonlyArray<number>, fallback: number): number {
  const seconds = Number(value);
  return value !== null && choices.includes(seconds) ? seconds : fallback;
}
