/** Rows per page in the online users and members tables. */
export const PAGE_SIZE = 25;

/** "Just now", "42s ago" or "3 min ago". */
export function formatAgo(ms: number) {
  const seconds = Math.max(0, Math.round(ms / 1000));
  if (seconds < 10) return "Just now";
  if (seconds < 60) return `${seconds}s ago`;
  return `${Math.round(seconds / 60)} min ago`;
}

export const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`;
