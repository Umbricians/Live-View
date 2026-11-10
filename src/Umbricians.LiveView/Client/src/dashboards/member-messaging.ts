import type { MemberMessaging } from "../api/live-view.resource.js";

/** Members can be messaged only with the permission and at least one delivery method set up by a developer. */
export function memberMessagingAvailable(permitted: boolean, messaging: MemberMessaging | undefined): boolean {
  return permitted && (messaging?.senders.length ?? 0) > 0;
}
