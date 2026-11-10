import { test } from "node:test";
import assert from "node:assert/strict";
import { INTERVALS, toInterval } from "./refresh-interval.ts";

test("an offered interval is kept, as a number or as stored text", () => {
  for (const seconds of INTERVALS) {
    assert.equal(toInterval(seconds), seconds);
    assert.equal(toInterval(String(seconds)), seconds);
  }
});

test("anything else is the default of 5 seconds", () => {
  for (const value of [null, "", "abc", 0, 7]) assert.equal(toInterval(value), 5);
});
