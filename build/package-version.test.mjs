// Tests for package-version.mjs, run by the build check with `node --test "build/*.test.mjs"`. Each test runs the
// script in a git repo of its own in a temp folder, with GitHub's ref variables and release tags set by the test.
import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const script = fileURLToPath(new URL("package-version.mjs", import.meta.url));

// Runs the script in dir for a project whose <Version> is projectVersion, with any options after it. CI's own
// GITHUB_* variables are dropped so they can't leak in (a tag build sets them).
function run(dir, github, projectVersion = "17.0.0", ...options) {
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !/^GITHUB_/i.test(key)));
  const { status, stdout, stderr } = spawnSync(process.execPath, [script, ...(projectVersion ? [projectVersion] : []), ...options], {
    cwd: dir,
    env: { ...env, ...github },
    encoding: "utf8",
  });
  return { status, stdout, stderr };
}

function git(dir, ...args) {
  return execFileSync(
    "git",
    ["-c", "user.name=Test", "-c", "user.email=test@example.test", "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false", ...args],
    { cwd: dir, stdio: "pipe", encoding: "utf8" },
  );
}

// A git repo on develop with one empty commit.
function repo() {
  const dir = mkdtempSync(join(tmpdir(), "package-version-"));
  git(dir, "init", "--quiet", "--initial-branch=develop");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Start");
  return dir;
}

const tagBuild = (name) => ({ GITHUB_REF_TYPE: "tag", GITHUB_REF_NAME: name, GITHUB_RUN_NUMBER: "42" });

// A tag build of name in a repo that already has the given release tags, for a project whose <Version> is
// projectVersion (CI passes the tag's major, so 18.0.0 for a v18 tag).
function tagVersion(name, { tags = [], projectVersion = "17.0.0" } = {}) {
  const dir = repo();
  for (const tag of tags) git(dir, "tag", tag);
  if (!tags.includes(name)) git(dir, "tag", name);
  return run(dir, tagBuild(name), projectVersion);
}

test("a tag builds its own version", () => {
  const { status, stdout, stderr } = tagVersion("v17.2.3", { tags: ["v17.2.2"] });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.2.3\n");
  assert.equal(stderr, "");
});

test("a pre-release tag builds its pre-release version", () => {
  const { status, stdout, stderr } = tagVersion("v17.3.0-beta.1", { tags: ["v17.2.0"] });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.3.0-beta.1\n");
});

for (const tag of ["v17.2", "v17.2.3.4", "v17.x.0"]) {
  test(`the tag ${tag} is rejected`, () => {
    const { status, stdout, stderr } = tagVersion(tag);
    const version = tag.slice(1);
    assert.equal(status, 1);
    assert.equal(stdout, "");
    assert.match(stderr, new RegExp(`'${version.replace(/\./g, "\.")}' is not a valid package version`));
  });
}

for (const tag of ["v0.9.0", "v18.0.0", "v1.0.0-beta.1"]) {
  test(`the tag ${tag} is rejected, since its major isn't the project's Umbraco major`, () => {
    const { status, stdout, stderr } = tagVersion(tag);
    assert.equal(status, 1);
    assert.equal(stdout, "");
    assert.match(stderr, /doesn't start with the Umbraco major 17 that the project's <Version> \(17\.0\.0\) supports/);
  });
}

for (const [tag, tags] of [
  ["v17.0.0", []],
  ["v17.2.1", ["v17.1.0", "v17.2.0"]],
  ["v17.3.0", ["v17.2.0", "v17.2.1"]],
  ["v17.3.0-rc.1", ["v17.2.0"]],
  ["v17.2.0", ["v17.1.0", "v17.2.0"]],
]) {
  test(`the tag ${tag} follows ${tags.at(-1) ?? "no release"} of its major`, () => {
    const { status, stderr } = tagVersion(tag, { tags });
    assert.equal(status, 0, stderr);
  });
}

for (const [tag, tags, next] of [
  ["v17.1.0", [], "17.0.0"],
  ["v17.2.2", ["v17.2.0"], "17.2.1 or 17.3.0"],
  ["v17.4.0", ["v17.2.0"], "17.2.1 or 17.3.0"],
  ["v17.2.0-beta.1", ["v17.2.0"], "17.2.1 or 17.3.0"],
  ["v17.1.1", ["v17.1.0", "v17.2.0"], "17.2.1 or 17.3.0"],
]) {
  test(`the tag ${tag} is rejected after ${tags.at(-1) ?? "no release"}, since it skips or repeats a release`, () => {
    const { status, stdout, stderr } = tagVersion(tag, { tags });
    assert.equal(status, 1);
    assert.equal(stdout, "");
    assert.match(stderr, new RegExp(`the next release is ${next}$`, "m"));
  });
}

// CI reads the project's <Version> for the tag's own major, so the major check alone can't catch a mistyped major.
test("a tag of the wrong major is rejected when it isn't that major's next release", () => {
  const { status, stdout, stderr } = tagVersion("v18.2.1", { tags: ["v17.2.0"], projectVersion: "18.0.0" });
  assert.equal(status, 1);
  assert.equal(stdout, "");
  assert.match(stderr, /'18\.2\.1' doesn't follow any release of Umbraco major 18: the next release is 18\.0\.0/);
});

test("the baseline is the major's last stable release, not counting the tag being built", () => {
  const dir = repo();
  for (const tag of ["v0.8.0", "v17.1.0", "v17.2.0", "v17.3.0-beta.1", "v18.0.0"]) git(dir, "tag", tag);
  const branchBuild = { GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop" };

  assert.equal(run(dir, branchBuild, "17.0.0", "--baseline").stdout, "17.2.0\n");
  assert.equal(run(dir, tagBuild("v17.2.0"), "17.0.0", "--baseline").stdout, "17.1.0\n");
  assert.equal(run(dir, tagBuild("v18.0.0"), "18.0.0", "--baseline").stdout, "\n");
  assert.equal(run(dir, branchBuild, "19.0.0", "--baseline").stdout, "\n");
});

test("a branch build is the patch after the highest stable tag of the project's major, reachable or not", () => {
  const dir = repo();
  git(dir, "tag", "v17.8.0");
  git(dir, "switch", "--quiet", "-c", "main");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Release");
  git(dir, "tag", "v17.10.0");
  git(dir, "tag", "v17.11.0-beta.1");
  git(dir, "switch", "--quiet", "develop");

  const { status, stdout, stderr } = run(dir, {
    GITHUB_REF_TYPE: "branch",
    GITHUB_REF_NAME: "develop",
    GITHUB_RUN_NUMBER: "42",
  });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.10.1-ci.42\n");
});

test("tags of other majors don't count", () => {
  const dir = repo();
  git(dir, "tag", "v0.8.0");
  git(dir, "tag", "v16.4.0");
  git(dir, "tag", "v17.1.0");
  git(dir, "tag", "v18.0.0");

  const { status, stdout, stderr } = run(dir, { GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: "7" });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.1.1-ci.7\n");
});

test("each Umbraco major builds from its own tags", () => {
  const dir = repo();
  git(dir, "tag", "v17.3.0");
  git(dir, "tag", "v18.1.0");
  const branchBuild = { GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: "7" };

  assert.equal(run(dir, branchBuild, "17.0.0").stdout, "17.3.1-ci.7\n");
  assert.equal(run(dir, branchBuild, "18.0.0").stdout, "18.1.1-ci.7\n");
  assert.equal(run(dir, branchBuild, "19.0.0").stdout, "19.0.0-ci.7\n");
});

test("before the major's first stable release, a build uses the project's <Version>", () => {
  const dir = repo();
  git(dir, "tag", "v0.8.0");
  git(dir, "tag", "v17.0.0-beta.1");

  const { status, stdout, stderr } = run(dir, { GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: "7" });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.0.0-ci.7\n");
});

test("outside CI, the run number is 0", () => {
  const dir = repo();
  git(dir, "tag", "v17.8.0");

  const { status, stdout, stderr } = run(dir, {});
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.8.1-ci.0\n");
});

test("with no stable tag and no project version, the build is rejected", () => {
  const dir = repo();

  const { status, stderr } = run(dir, { GITHUB_RUN_NUMBER: "7" }, "");
  assert.equal(status, 1);
  assert.match(stderr, /'-ci\.7' is not a valid package version/);
});
