import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import en from "./en.ts";
import { LIVE_VIEW_PERMISSION_ENTITY_TYPE } from "../permissions/constants.ts";

const terms = en.umbriciansLiveView as Record<string, string | ((...args: Array<any>) => string)>;

const srcDirectory = fileURLToPath(new URL("..", import.meta.url));
const source = readdirSync(srcDirectory, { recursive: true, encoding: "utf8" })
  .map((path) => path.replaceAll("\\", "/"))
  .filter((path) => path.endsWith(".ts") && !path.endsWith(".test.ts") && path !== "localization/en.ts")
  .map((path) => readFileSync(new URL(path, new URL("..", import.meta.url)), "utf8"))
  .join("\n");
const used = new Set([...source.matchAll(/umbriciansLiveView_(\w+)/g)].map((match) => match[1]));

test("every term the source uses is in the English dictionary", () => {
  assert.ok(used.size > 0);
  const missing = [...used].filter((key) => !(key in terms));
  assert.deepEqual(missing, []);
});

test("every term in the English dictionary is used", () => {
  const unused = Object.keys(terms).filter((key) => !used.has(key));
  assert.deepEqual(unused, []);
});

test("the permission box is headed by the term for the entity type", () => {
  assert.ok(`permissionsEntityGroup_${LIVE_VIEW_PERMISSION_ENTITY_TYPE}` in en.user);
});

test("counted terms read right for one and for more", () => {
  const term = (key: string) => terms[key] as (count: number) => string;
  assert.equal(term("usersSelected")(1), "1 user selected");
  assert.equal(term("usersSelected")(2), "2 users selected");
  assert.equal(term("sentToUsers")(1), "Sent to 1 user.");
  assert.equal(term("sentToUsers")(2), "Sent to 2 users.");
  assert.equal(term("sendToMembers")(1), "Send to 1 member");
  assert.equal(term("sendToMembers")(2), "Send to 2 members");
  assert.equal(term("allOnlineUsersSelected")(1), "All 1 online user is selected.");
  assert.equal(term("allOnlineUsersSelected")(2), "All 2 online users are selected.");
  assert.equal(term("secondsAgo")(42), "42s ago");
  assert.equal(term("minutesAgo")(3), "3 min ago");
  assert.equal(term("durationDays")(1), "1 day");
  assert.equal(term("durationDays")(3), "3 days");
  assert.equal(term("durationHours")(3), "3 h");
  assert.equal(term("durationMinutes")(48), "48 min");
});

test("a sender's error is lowercased", () => {
  const failed = terms.senderFailed as (sender: string, error: string) => string;
  assert.equal(failed("Email", "Mailbox Full"), "Email: mailbox full");
});
