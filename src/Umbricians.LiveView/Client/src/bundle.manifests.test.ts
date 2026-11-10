import { test } from "node:test";
import assert from "node:assert/strict";
import { manifests } from "./bundle.manifests.ts";

test("every extension alias carries the package id", () => {
  const strays = manifests.map((manifest) => manifest.alias).filter((alias) => !alias.startsWith("Umbricians.LiveView."));
  assert.deepEqual(strays, []);
});

test("no two extensions share an alias", () => {
  const aliases = manifests.map((manifest) => manifest.alias);
  assert.equal(new Set(aliases).size, aliases.length);
});
