import { test } from "node:test";
import assert from "node:assert/strict";
import type { MemberMessaging } from "../api/live-view.resource.ts";
import { memberMessagingAvailable } from "./member-messaging.ts";

const withSender = { senders: [{}], totalMembers: 1, timeZone: null } as unknown as MemberMessaging;
const withoutSenders: MemberMessaging = { senders: [], totalMembers: 1, timeZone: null };

test("members can be messaged with the permission and a sender", () => {
  assert.equal(memberMessagingAvailable(true, withSender), true);
});

test("not without the permission, even with a sender", () => {
  assert.equal(memberMessagingAvailable(false, withSender), false);
});

test("not with the permission but no senders", () => {
  assert.equal(memberMessagingAvailable(true, withoutSenders), false);
});

test("not while the messaging setup is unknown", () => {
  assert.equal(memberMessagingAvailable(true, undefined), false);
});
