// Tests for check-bundle.mjs, run by `npm test`. CI runs the tests before the build, so each test checks a small
// folder written here rather than the real output: one the check accepts, then the same folder with the API entry
// broken the ways it can break.
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const script = fileURLToPath(new URL("check-bundle.mjs", import.meta.url));
const apiEntry = "umbricians-live-view-api.js";

// The smallest output every rule passes on: the bundle entry, a source map, one decorated field applied the way the
// compiled decorators apply it, and the API entry exporting what src/index.ts exports.
const goodFiles = {
  "umbricians-live-view.js": 'const manifests = [];\nexport { manifests };\n',
  "umbricians-live-view.js.map": JSON.stringify({ version: 3, sources: ["../../../Client/src/bundle.manifests.ts"], mappings: "" }),
  "element.js":
    'import { property } from "@umbraco-cms/backoffice/external/lit";\n' +
    'import { d } from "./decorate.js";\n' +
    "class Element {}\n" +
    'd([property()], Element.prototype, "label", void 0);\n' +
    "export { Element };\n",
  "decorate.js": "function d(decorators, target, key, desc) { return desc; }\nexport { d };\n",
  [apiEntry]: 'export { t as UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT } from "./context-token.js";\n',
  "context-token.js": 'const t = "token";\nexport { t };\n',
};

function check(changes = {}) {
  const dir = mkdtempSync(join(tmpdir(), "check-bundle-"));
  try {
    for (const [name, content] of Object.entries({ ...goodFiles, ...changes })) {
      if (content !== null) writeFileSync(join(dir, name), content);
    }
    const result = spawnSync(process.execPath, [script, dir], { encoding: "utf8" });
    return { status: result.status, output: result.stdout + result.stderr };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

test("passes a folder that keeps every rule", () => {
  const { status, output } = check();
  assert.equal(status, 0, output);
  assert.match(output, /@umbricians\/live-view exports what src\/index\.ts does/);
});

test("fails when the API entry the import map names is missing", () => {
  const { status, output } = check({ [apiEntry]: null });
  assert.equal(status, 1);
  assert.match(output, /umbricians-live-view-api\.js, which the import map points @umbricians\/live-view at, is missing from the build/);
});

test("fails when the API entry exports nothing", () => {
  const { status, output } = check({ [apiEntry]: 'import "./context-token.js";\n' });
  assert.equal(status, 1);
  assert.match(output, /umbricians-live-view-api\.js exports nothing, but @umbricians\/live-view should export UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT/);
});

test("fails when the API entry exports other names", () => {
  const { status, output } = check({ [apiEntry]: 'export { t as SOMETHING_ELSE } from "./context-token.js";\n' });
  assert.equal(status, 1);
  assert.match(output, /umbricians-live-view-api\.js exports SOMETHING_ELSE, but @umbricians\/live-view should export UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT/);
});

test("fails when the API entry exports more than src/index.ts", () => {
  const { status, output } = check({
    [apiEntry]: 'export { t as UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT, t as EXTRA } from "./context-token.js";\n',
  });
  assert.equal(status, 1);
  assert.match(output, /exports EXTRA, UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT, but @umbricians\/live-view should export UMBRICIANS_LIVE_VIEW_DASHBOARD_CONTEXT/);
});

test("fails when the API entry re-exports a whole module", () => {
  const { status, output } = check({ [apiEntry]: 'export * from "./context-token.js";\n' });
  assert.equal(status, 1);
  assert.match(output, /umbricians-live-view-api\.js exports \* from \.\/context-token\.js, but @umbricians\/live-view should export/);
});
