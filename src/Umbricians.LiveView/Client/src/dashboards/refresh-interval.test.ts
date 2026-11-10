import { test } from "node:test";
import assert from "node:assert/strict";
import { BUILT_IN_REFRESH_SETTINGS, INTERVALS, toInterval } from "./refresh-interval.ts";

test("an offered interval is kept, as a number or as stored text", () => {
  for (const seconds of INTERVALS) {
    assert.equal(toInterval(seconds, BUILT_IN_REFRESH_SETTINGS), seconds);
    assert.equal(toInterval(String(seconds), BUILT_IN_REFRESH_SETTINGS), seconds);
  }
});

test("anything else is the default of 5 seconds", () => {
  for (const value of [null, "", "abc", 0, 7]) assert.equal(toInterval(value, BUILT_IN_REFRESH_SETTINGS), 5);
});

test("the built-in settings are used when none are given", () => {
  assert.equal(toInterval(10), 10);
  assert.equal(toInterval(7), 5);
});

test("a remembered interval that is still offered is kept", () => {
  assert.equal(toInterval("10", { refreshIntervals: [2, 5, 10], defaultRefreshInterval: 5 }), 10);
});

test("a remembered interval the site no longer offers falls back to the default", () => {
  assert.equal(toInterval("15", { refreshIntervals: [5, 30, 60], defaultRefreshInterval: 30 }), 30);
});

test("nothing remembered uses the default", () => {
  assert.equal(toInterval(null, { refreshIntervals: [2, 5, 10], defaultRefreshInterval: 5 }), 5);
});

test("an unreadable value uses the default", () => {
  const settings = { refreshIntervals: [2, 5, 10], defaultRefreshInterval: 5 };
  assert.equal(toInterval("abc", settings), 5);
  assert.equal(toInterval("", settings), 5);
});
