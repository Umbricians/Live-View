import { test } from "node:test";
import assert from "node:assert/strict";
import { LIVE_VIEW_PANEL_TYPE } from "./constants.ts";
import { manifests } from "./manifest.ts";
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
  const dashboard = manifests.find((m) => m.alias === "Umbricians.LiveView.Dashboard");

  assert.deepEqual(dashboard?.conditions, [
    { alias: "Umb.Condition.SectionAlias", match: "Umb.Section.Settings" },
    { alias: LIVE_VIEW_PERMISSION_CONDITION_ALIAS, permission: LIVE_VIEW_PERMISSION_VIEW },
  ]);
});
