import assert from "node:assert/strict";
import { test } from "node:test";
import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import en from "../localization/en.ts";
import { LiveViewRequestError, describeError } from "./request-error.ts";

const terms = en.umbriciansLiveView as Record<string, string | ((...args: Array<any>) => string)>;
const localize = {
  term: (key: string, ...args: Array<unknown>) => {
    const term = terms[key.replace(/^umbriciansLiveView_/, "")];
    return typeof term === "function" ? term(...args) : term;
  },
} as unknown as UmbLocalizationController;

test("the server's own explanation is shown as it is", () => {
  assert.equal(describeError(localize, new LiveViewRequestError(400, "The headline is required.")), "The headline is required.");
});

test("a failure with a status and no explanation names the status", () => {
  assert.equal(describeError(localize, new LiveViewRequestError(500, undefined)), "Live View request failed (500)");
});

test("a network error reads as no response, not the browser's own wording", () => {
  assert.equal(describeError(localize, new TypeError("Failed to fetch")), "Live View request failed (no response)");
});

test("a request error without a status reads as no response", () => {
  assert.equal(describeError(localize, new LiveViewRequestError(undefined, undefined)), "Live View request failed (no response)");
});
