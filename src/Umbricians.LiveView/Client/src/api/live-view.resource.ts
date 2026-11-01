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
  /** The user's avatar in Umbraco's sizes, smallest first; empty when they have not set one. */
  avatarUrls: Array<string>;
  lastSeen: string;
  openTabs: number;
  isConnected: boolean;
  isCurrentUser: boolean;
  /** When the user was last seen in the backoffice, or null when they are only on MCP. */
  backofficeLastSeen: string | null;
  /** When the user last used MCP, or null when they are not on it. */
  mcpLastSeen: string | null;
}

export interface OnlineMember {
  key: string;
  name: string;
  email: string | null;
  memberType: string | null;
  /** The member's picture from the site's avatar provider, or null to show their initials. */
  avatarUrl: string | null;
  lastSeen: string;
  /** When the member was last seen on the website, or null when they are only on MCP. */
  websiteLastSeen: string | null;
  /** When the member last used MCP, or null when they are not on it. */
  mcpLastSeen: string | null;
}

/** Matches Umbraco's PagedViewModel. */
export interface PagedResult<T> {
  total: number;
  items: Array<T>;
}

export interface OnlineMembers extends PagedResult<OnlineMember> {
  /** Whether anyone online is using MCP; only then do rows say how each member is online. */
  anyOnMcp: boolean;
}

export interface OnlineBackofficeUsers extends PagedResult<OnlineBackofficeUser> {
  /** Whether anyone online is using MCP; only then do rows say how each user is online. */
  anyOnMcp: boolean;
  /** How many users a message to everyone online reaches: everyone online except the current user. */
  recipients: number;
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
    // The client throws the error body as it is by default; returning it lets every failure become an Error below.
    throwOnError: false,
    ...options,
  });

  if (error !== undefined || !response?.ok) {
    // A message that can't be sent says why in the ProblemDetails detail, which the dashboard shows as it is.
    const detail = (error as { detail?: unknown } | undefined)?.detail;
    throw new Error(typeof detail === "string" && detail ? detail : `Live View request failed (${response?.status ?? "no response"})`);
  }
  return data as T;
}

export const fetchSnapshot = (signal?: AbortSignal) => request<LiveViewSnapshot>("GET", "snapshot", { signal });

export const fetchOnlineUsers = (skip: number, take: number, signal?: AbortSignal) =>
  request<OnlineBackofficeUsers>("GET", `backoffice-users?skip=${skip}&take=${take}`, { signal });

export const fetchOnlineMembers = (skip: number, take: number, signal?: AbortSignal) =>
  request<OnlineMembers>("GET", `members?skip=${skip}&take=${take}`, { signal });

/** Sends to the given users, or to everyone online when `allOnline` is set. */
export const sendMessage = (body: {
  headline: string;
  message: string;
  color: MessageColor;
  userKeys?: Array<string>;
  allOnline?: boolean;
}) => request<{ key: string; recipients: number }>("POST", "messages", { body });

export interface MemberMessageSender {
  alias: string;
  name: string;
  description: string | null;
  reachesOfflineMembers: boolean;
}

export interface MemberMessaging {
  senders: Array<MemberMessageSender>;
  totalMembers: number;
  /** The IANA time zone "midnight tonight" is worked out in, or null for the sender's own. */
  timeZone: string | null;
}

export type MemberAudience = "selected" | "allOnline" | "allMembers";

export interface MemberMessageSendResult {
  key: string;
  recipients: number | null;
  results: Array<{ alias: string; name: string; delivered: number | null; note: string | null; error: string | null }>;
}

/** The registered member message senders; an empty list means member messaging is unavailable. */
export const fetchMemberMessaging = () => request<MemberMessaging>("GET", "member-messaging");

export const sendMemberMessage = (body: {
  headline: string;
  message: string;
  color: MessageColor;
  audience: MemberAudience;
  memberKeys?: Array<string>;
  senders: Array<string>;
  /** When the message stops being relevant, as an ISO date: in the future, and no more than 30 days away. */
  expiresAt: string;
}) => request<MemberMessageSendResult>("POST", "member-messages", { body });

export const heartbeat = (tabId: string) =>
  request<{ pendingMessages: Array<string> }>("GET", `heartbeat?tab=${encodeURIComponent(tabId)}`);

/** Tells the server a tab is closing. Uses keepalive so the request survives the page unloading. */
export const endTab = (tabId: string) =>
  request<void>("DELETE", `heartbeat?tab=${encodeURIComponent(tabId)}`, { keepalive: true }).catch(() => {
    // Best effort; the tab times out on the server anyway.
  });

export const fetchMessage = (key: string) =>
  request<BackofficeMessage>("GET", `messages/${encodeURIComponent(key)}`);
