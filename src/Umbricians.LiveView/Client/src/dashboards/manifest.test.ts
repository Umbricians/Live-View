import { test } from "node:test";
import assert from "node:assert/strict";
import { LIVE_VIEW_PANEL_TYPE } from "./constants.ts";
import { LIVE_VIEW_DASHBOARD_ALIAS, LIVE_VIEW_DASHBOARD_KIND, manifests } from "./manifest.ts";
import { LIVE_VIEW_PERMISSION_CONDITION_ALIAS, LIVE_VIEW_PERMISSION_VIEW } from "../permissions/constants.ts";

test("the panel extension type is umbriciansLiveViewPanel", () => {
  assert.equal(LIVE_VIEW_PANEL_TYPE, "umbriciansLiveViewPanel");
});

test("the built-in panels are registered in their order, each with an element", () => {
  const panels = manifests.filter((m) => m.type === LIVE_VIEW_PANEL_TYPE) as unknown as Array<{
    alias: string;
    weight: number;
    element: unknown;
  }>;

  assert.deepEqual(
    panels.map((p) => [p.alias, p.weight]),
    [
      ["Umbricians.LiveView.Panel.Activity", 300],
      ["Umbricians.LiveView.Panel.Users", 200],
      ["Umbricians.LiveView.Panel.Members", 100],
    ],
  );
  for (const panel of panels) assert.equal(typeof panel.element, "function");
});

test("the dashboard is in the Settings section and needs the view permission", () => {
  const dashboard = manifests.find((m) => m.alias === LIVE_VIEW_DASHBOARD_ALIAS);

  assert.equal(LIVE_VIEW_DASHBOARD_ALIAS, "Umbricians.LiveView.Dashboard"); // sites exclude the dashboard by this string
  assert.deepEqual(dashboard?.conditions, [
    { alias: "Umb.Condition.SectionAlias", match: "Umb.Section.Settings" },
    { alias: LIVE_VIEW_PERMISSION_CONDITION_ALIAS, permission: LIVE_VIEW_PERMISSION_VIEW },
  ]);
});

test("the dashboard comes from the umbriciansLiveView kind, which brings the dashboard element", () => {
  const kind = manifests.find((m) => m.alias === "Umbricians.LiveView.Kind.Dashboard") as unknown as {
    type: string;
    matchType: string;
    matchKind: string;
    manifest: { type: string; js: unknown; weight: number };
  };

  assert.equal(LIVE_VIEW_DASHBOARD_KIND, "umbriciansLiveView"); // docs/getting-started.md tells sites to use this string
  assert.equal(kind.type, "kind");
  assert.equal(kind.matchType, "dashboard");
  assert.equal(kind.matchKind, LIVE_VIEW_DASHBOARD_KIND);
  assert.equal(kind.manifest.type, "dashboard");
  assert.equal(kind.manifest.weight, -100);
  assert.equal(typeof kind.manifest.js, "function");

  const dashboard = manifests.find((m) => m.alias === LIVE_VIEW_DASHBOARD_ALIAS) as unknown as { kind?: string };
  assert.equal(dashboard.kind, LIVE_VIEW_DASHBOARD_KIND);
});
