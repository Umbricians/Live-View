import assert from "node:assert/strict";
import { test } from "node:test";
import { durationParts, nextMidnight, timeZoneCity } from "./expiry.ts";

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

test("durationParts gives days and hours", () => {
  assert.deepEqual(durationParts(3 * DAY + 4 * HOUR), [
    { value: 3, unit: "day" },
    { value: 4, unit: "hour" },
  ]);
});

test("durationParts leaves out hours when there are none", () => {
  assert.deepEqual(durationParts(2 * DAY), [{ value: 2, unit: "day" }]);
});

test("durationParts gives hours and minutes", () => {
  assert.deepEqual(durationParts(5 * HOUR + 12 * MINUTE), [
    { value: 5, unit: "hour" },
    { value: 12, unit: "minute" },
  ]);
});

test("durationParts leaves out minutes when there are none", () => {
  assert.deepEqual(durationParts(2 * HOUR), [{ value: 2, unit: "hour" }]);
});

test("durationParts gives minutes alone under an hour", () => {
  assert.deepEqual(durationParts(12 * MINUTE), [{ value: 12, unit: "minute" }]);
});

test("durationParts is never negative", () => {
  assert.deepEqual(durationParts(-5 * MINUTE), [{ value: 0, unit: "minute" }]);
});

test("timeZoneCity names the city of a zone", () => {
  assert.equal(timeZoneCity("America/Denver"), "Denver");
  assert.equal(timeZoneCity("America/Los_Angeles"), "Los Angeles");
  assert.equal(timeZoneCity(undefined), undefined);
});

test("nextMidnight is the next midnight in the zone, across daylight saving changes", () => {
  const next = (iso: string) => nextMidnight(new Date(iso), "America/Denver").toISOString();
  // Ordinary day in winter (UTC-7) and summer (UTC-6).
  assert.equal(next("2025-01-15T20:00:00Z"), "2025-01-16T07:00:00.000Z");
  assert.equal(next("2025-07-15T20:00:00Z"), "2025-07-16T06:00:00.000Z");
  // Clocks go forward on 9 March 2025: midnight that night is still before the change, midnight after is in summer time.
  assert.equal(next("2025-03-08T20:00:00Z"), "2025-03-09T07:00:00.000Z");
  assert.equal(next("2025-03-09T12:00:00Z"), "2025-03-10T06:00:00.000Z");
  // Clocks go back on 2 November 2025.
  assert.equal(next("2025-11-01T20:00:00Z"), "2025-11-02T06:00:00.000Z");
  assert.equal(next("2025-11-02T20:00:00Z"), "2025-11-03T07:00:00.000Z");
});
