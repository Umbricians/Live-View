import { test } from "node:test";
import assert from "node:assert/strict";
import { offeredInterval } from "./refresh-interval.ts";

test("a remembered interval that is still offered is kept", () => {
  assert.equal(offeredInterval("10", [2, 5, 10], 5), 10);
});

test("a remembered interval the site no longer offers falls back to the default", () => {
  assert.equal(offeredInterval("15", [5, 30, 60], 30), 30);
});

test("nothing remembered uses the default", () => {
  assert.equal(offeredInterval(null, [2, 5, 10], 5), 5);
});

test("an unreadable value uses the default", () => {
  assert.equal(offeredInterval("abc", [2, 5, 10], 5), 5);
  assert.equal(offeredInterval("", [2, 5, 10], 5), 5);
});
