import assert from "node:assert/strict";
import { test } from "node:test";
import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import en from "../localization/en.ts";
import { formatAgo } from "./format.ts";

const terms = en.umbriciansLiveView as Record<string, string | ((...args: Array<any>) => string)>;
const localize = {
  term: (key: string, ...args: Array<unknown>) => {
    const term = terms[key.replace(/^umbriciansLiveView_/, "")];
    return typeof term === "function" ? term(...args) : term;
  },
} as unknown as UmbLocalizationController;

test("formatAgo says just now under ten seconds", () => {
  assert.equal(formatAgo(localize, 5_000), "Just now");
});

test("formatAgo says seconds under a minute", () => {
  assert.equal(formatAgo(localize, 42_000), "42s ago");
});

test("formatAgo says minutes from a minute on", () => {
  assert.equal(formatAgo(localize, 180_000), "3 min ago");
});

test("formatAgo treats a negative age as just now", () => {
  assert.equal(formatAgo(localize, -5_000), "Just now");
});
