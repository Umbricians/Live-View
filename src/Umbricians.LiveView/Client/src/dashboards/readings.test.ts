import { test } from "node:test";
import assert from "node:assert/strict";
import { addReading, readingsAfter } from "./readings.ts";

const reading = (timestamp: string) => ({ timestamp, metrics: [] });
const a = reading("2026-01-01T00:00:00Z");
const b = reading("2026-01-01T00:00:05Z");
const c = reading("2026-01-01T00:00:10Z");

test("addReading puts the newest at the end", () => {
  assert.deepEqual(addReading([a], b), [a, b]);
});

test("addReading drops the oldest once past max, keeping exactly max", () => {
  const result = addReading([a, b], c, 2);
  assert.deepEqual(result, [b, c]);
  assert.equal(result.length, 2);
});

test("addReading doesn't change the array it was given", () => {
  const readings = [a, b];
  addReading(readings, c, 2);
  assert.deepEqual(readings, [a, b]);
});

test("readingsAfter returns all readings when nothing was shown yet", () => {
  assert.deepEqual(readingsAfter([a, b], undefined), [a, b]);
});

test("readingsAfter returns only readings after last, even when two share a timestamp", () => {
  const twin = reading(a.timestamp);
  assert.deepEqual(readingsAfter([a, twin, b], a), [twin, b]);
  assert.deepEqual(readingsAfter([a, twin, b], twin), [b]);
});

test("readingsAfter returns nothing when last is the newest", () => {
  assert.deepEqual(readingsAfter([a, b], b), []);
});

test("readingsAfter returns all readings when last was dropped", () => {
  assert.deepEqual(readingsAfter([b, c], a), [b, c]);
});
