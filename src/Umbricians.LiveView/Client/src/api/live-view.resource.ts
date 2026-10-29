import { umbHttpClient } from "@umbraco-cms/backoffice/http-client";

export interface LiveMetricReading {
  alias: string;
  name: string;
  color: string | null;
  value: number | null;
  error: string | null;
}

export interface LiveViewSnapshot {
  timestamp: string;
  metrics: Array<LiveMetricReading>;
}

export interface OnlineBackofficeUser {
  key: string;
  name: string;
  email: string | null;
  lastSeen: string;
  openTabs: number;
  isConnected: boolean;
  isCurrentUser: boolean;
}

export interface OnlineMember {
  key: string;
  name: string;
  email: string | null;
  memberType: string | null;
  lastSeen: string;
}

/** Matches Umbraco's PagedViewModel. */
export interface PagedResult<T> {
  total: number;
  items: Array<T>;
}

export type MessageColor = "default" | "positive" | "warning" | "danger";

export interface BackofficeMessage {
  key: string;
  headline: string;
  message: string;
  color: MessageColor;
  senderName: string | null;
  sentAt: string;
}

const BASE_URL = "/umbraco/umbriciansliveview/api/v1/";

/**
 * Calls the Live View API through the backoffice's own pre-configured HTTP client, so requests carry the
 * user's session and an expired access token is refreshed and the request retried, exactly as for core calls.
 */
async function request<T>(
  method: "GET" | "POST" | "DELETE",
  path: string,
  options: { body?: unknown; signal?: AbortSignal; keepalive?: boolean } = {},
): Promise<T> {
  const { data, error, response } = await umbHttpClient.request({
    method,
    url: BASE_URL + path,
    // Tells the client to apply the backoffice authentication to this call.
    security: [{ type: "http", scheme: "bearer" }],
    ...options,
  });

  if (error !== undefined || !response?.ok) {
    throw new Error(`Live View request failed (${response?.status ?? "no response"})`);
  }
  return data as T;
}

export const fetchSnapshot = (signal?: AbortSignal) => request<LiveViewSnapshot>("GET", "snapshot", { signal });

export const fetchOnlineUsers = (skip: number, take: number, signal?: AbortSignal) =>
  request<PagedResult<OnlineBackofficeUser>>("GET", `backoffice-users?skip=${skip}&take=${take}`, { signal });

export const fetchOnlineMembers = (skip: number, take: number, signal?: AbortSignal) =>
  request<PagedResult<OnlineMember>>("GET", `members?skip=${skip}&take=${take}`, { signal });

/** Sends to the given users, or to everyone online when `allOnline` is set. */
export const sendMessage = (body: {
  headline: string;
  message: string;
  color: MessageColor;
  userKeys?: Array<string>;
  allOnline?: boolean;
}) => request<{ key: string; recipients: number }>("POST", "messages", { body });

export const heartbeat = (tabId: string) =>
  request<{ pendingMessages: Array<string> }>("GET", `heartbeat?tab=${encodeURIComponent(tabId)}`);

/** Tells the server a tab is closing. Uses keepalive so the request survives the page unloading. */
export const endTab = (tabId: string) =>
  request<void>("DELETE", `heartbeat?tab=${encodeURIComponent(tabId)}`, { keepalive: true }).catch(() => {
    // Best effort; the tab times out on the server anyway.
  });

export const fetchMessage = (key: string) =>
  request<BackofficeMessage>("GET", `messages/${encodeURIComponent(key)}`);
