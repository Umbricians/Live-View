import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from "@umbraco-cms/backoffice/extension-api";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { UMB_MANAGEMENT_API_SERVER_EVENT_CONTEXT } from "@umbraco-cms/backoffice/management-api";
import { endTab, fetchMessage, heartbeat } from "../api/live-view.resource.js";

// Must match BackofficeMessenger.EventSource and EventType on the server.
const EVENT_SOURCE = "Umbricians.LiveView";
const EVENT_TYPE = "Message";
const HEARTBEAT_MS = 60_000;

// Identifies this tab so the dashboard can count each user's open tabs. A new one per page load.
const TAB_ID = crypto.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

let heartbeatTimer: number | undefined;
let removeListeners: (() => void) | undefined;

/**
 * Runs in every backoffice tab, for every user: sends a heartbeat so the user and this tab show as online on the
 * Live View dashboard, and shows messages sent from it as notifications that stay until dismissed.
 */
export const onInit: UmbEntryPointOnInit = (host) => {
  let notifications: typeof UMB_NOTIFICATION_CONTEXT.TYPE | undefined;
  const shown = new Set<string>();

  const show = async (key: string) => {
    if (!notifications || shown.has(key)) return;
    shown.add(key);
    try {
      const message = await fetchMessage(key);
      const from = message.senderName ? `\n\n– ${message.senderName}` : "";
      notifications.stay(message.color, { data: { headline: message.headline, message: message.message + from } });
    } catch {
      // Expired or not addressed to this user; let a later heartbeat retry if it is still pending.
      shown.delete(key);
    }
  };

  const beat = async () => {
    try {
      const { pendingMessages } = await heartbeat(TAB_ID);
      pendingMessages.forEach(show);
    } catch {
      // Signed out or offline; the next beat tries again.
    }
  };

  // Closing the tab removes it at once; if the browser restores it from its back/forward cache, re-register it.
  const onPageHide = () => endTab(TAB_ID);
  const onPageShow = (event: PageTransitionEvent) => event.persisted && beat();
  window.addEventListener("pagehide", onPageHide);
  window.addEventListener("pageshow", onPageShow);
  removeListeners = () => {
    window.removeEventListener("pagehide", onPageHide);
    window.removeEventListener("pageshow", onPageShow);
  };

  host.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (notifications = context));
  host.consumeContext(UMB_MANAGEMENT_API_SERVER_EVENT_CONTEXT, (context) => {
    // Not byEventSource(): in 17.8 it filters on the event type despite its name.
    context?.byEventSourcesAndEventTypes([EVENT_SOURCE], [EVENT_TYPE]).subscribe((event) => show(event.key));
  });

  beat();
  heartbeatTimer = window.setInterval(beat, HEARTBEAT_MS);
};

export const onUnload: UmbEntryPointOnUnload = () => {
  window.clearInterval(heartbeatTimer);
  removeListeners?.();
};
