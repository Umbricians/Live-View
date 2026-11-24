// Tests for release-options.mjs, run by the build check with `node --test "build/*.test.mjs"`. Each test passes the
// stable releases on standard input, the way the release job pipes them from `gh release list`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const script = fileURLToPath(new URL("release-options.mjs", import.meta.url));

function run(tag, input) {
  return spawnSync(process.execPath, [script, tag], { input, encoding: "utf8" });
}

function options(tag, releases) {
  const { status, stdout, stderr } = run(tag, JSON.stringify(releases.map((tagName) => ({ tagName }))));
  assert.equal(status, 0, stderr);
  return stdout.split("\n").filter(Boolean);
}

test("the first release of the first major is Latest, with notes from the start", () => {
  assert.deepEqual(options("v17.0.0", ["v0.8.0", "v0.7.0"]), ["--latest"]);
});

test("a release's notes start at the last release of its own major", () => {
  assert.deepEqual(options("v17.3.0", ["v18.1.0", "v17.2.1", "v17.10.0", "v17.2.0"]), ["--notes-start-tag=v17.10.0", "--latest=false"]);
});

test("a release of an older major isn't Latest", () => {
  assert.deepEqual(options("v17.9.1", ["v18.1.0", "v17.9.0"]), ["--notes-start-tag=v17.9.0", "--latest=false"]);
});

test("a release of the newest major is Latest", () => {
  assert.deepEqual(options("v18.1.1", ["v18.1.0", "v17.9.0"]), ["--notes-start-tag=v18.1.0", "--latest"]);
});

test("the first release of a new major is Latest", () => {
  assert.deepEqual(options("v19.0.0", ["v18.1.0", "v17.9.0"]), ["--latest"]);
});

test("a pre-release is a GitHub pre-release and never Latest", () => {
  assert.deepEqual(options("v19.0.0-rc.1", ["v18.1.0"]), ["--prerelease"]);
  assert.deepEqual(options("v17.10.0-beta.1", ["v17.9.0", "v18.1.0"]), ["--prerelease", "--notes-start-tag=v17.9.0"]);
});

test("a re-run doesn't count its own release", () => {
  assert.deepEqual(options("v17.3.0", ["v17.3.0", "v17.2.0"]), ["--notes-start-tag=v17.2.0", "--latest"]);
});

test("no input fails, since it means listing the releases failed", () => {
  for (const input of ["", "\n"]) {
    const { status, stdout, stderr } = run("v17.9.1", input);
    assert.equal(status, 1);
    assert.equal(stdout, "");
    assert.match(stderr, /No releases were given on standard input/);
  }
});

test("with no releases at all, a release is Latest", () => {
  assert.deepEqual(options("v17.0.0", []), ["--latest"]);
});
