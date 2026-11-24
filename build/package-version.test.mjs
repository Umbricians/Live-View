// Tests for package-version.mjs, run by the build check with `node --test "build/*.test.mjs"`. Each test runs the
// script in a git repo of its own in a temp folder, with GitHub's ref variables, release tags and the versions
// nuget.org has (PACKAGE_VERSIONS) set by the test.
import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const script = fileURLToPath(new URL("package-version.mjs", import.meta.url));

// Runs the script in dir. The project's <Version> is project (CI passes the major being built, so 18.0.0 for a v18
// tag) and its lowest Umbraco version umbraco; published lists the versions nuget.org has. CI's own GITHUB_*
// variables are dropped so they can't leak in (a tag build sets them).
function run(dir, github, { project = "17.0.0", umbraco = `${project.split(".")[0]}.8.2`, published = [], baseline = false } = {}) {
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !/^GITHUB_/i.test(key)));
  const args = [script, "Umbricians.Example", project, umbraco, ...(baseline ? ["--baseline"] : [])];
  const { status, stdout, stderr } = spawnSync(process.execPath, args, {
    cwd: dir,
    env: { ...env, ...github, PACKAGE_VERSIONS: published.join(",") },
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

// A git repo on develop with one empty commit and the given tags.
function repo(tags = []) {
  const dir = mkdtempSync(join(tmpdir(), "package-version-"));
  git(dir, "init", "--quiet", "--initial-branch=develop");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Start");
  for (const tag of tags) git(dir, "tag", tag);
  return dir;
}

const tagBuild = (name) => ({ GITHUB_REF_TYPE: "tag", GITHUB_REF_NAME: name, GITHUB_RUN_NUMBER: "42" });
const branchBuild = (run = "7") => ({ GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: run });

// Each version is published and tagged, as a release normally is.
const released = (...versions) => ({ tags: versions.map((version) => `v${version}`), published: versions });

// A tag build of name, after the given tags and published versions. The tag being built is created too.
function tagVersion(name, { tags = [], published = [], ...options } = {}) {
  const dir = repo([...tags, ...(tags.includes(name) ? [] : [name])]);
  return run(dir, tagBuild(name), { published, ...options });
}

test("a tag builds its own version", () => {
  const { status, stdout, stderr } = tagVersion("v17.2.3", released("17.2.2"));
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.2.3\n");
  assert.equal(stderr, "");
});

test("a pre-release tag builds its pre-release version", () => {
  const { status, stdout, stderr } = tagVersion("v17.3.0-beta.1", released("17.2.0"));
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.3.0-beta.1\n");
});

for (const tag of ["v17.2", "v17.2.3.4", "v17.x.0"]) {
  test(`the tag ${tag} is rejected`, () => {
    const { status, stdout, stderr } = tagVersion(tag);
    const version = tag.slice(1);
    assert.equal(status, 1);
    assert.equal(stdout, "");
    assert.match(stderr, new RegExp(`'${version.replace(/\./g, "\\.")}' is not a valid package version`));
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

for (const [tag, history] of [
  ["v17.0.0", released()],
  ["v17.2.1", released("17.1.0", "17.2.0")],
  ["v17.3.0", released("17.2.0", "17.2.1")],
  ["v17.3.0-rc.1", released("17.2.0")],
  ["v17.2.0", released("17.1.0", "17.2.0")],
]) {
  test(`the tag ${tag} follows ${history.published.at(-1) ?? "no release"} of its major`, () => {
    const { status, stderr } = tagVersion(tag, history);
    assert.equal(status, 0, stderr);
  });
}

for (const [tag, history, next] of [
  ["v17.1.0", released(), "17.0.0"],
  ["v17.2.2", released("17.2.0"), "17.2.1 or 17.3.0"],
  ["v17.4.0", released("17.2.0"), "17.2.1 or 17.3.0"],
  ["v17.2.0-beta.1", released("17.2.0"), "17.2.1 or 17.3.0"],
  ["v17.1.1", released("17.1.0", "17.2.0"), "17.2.1 or 17.3.0"],
]) {
  test(`the tag ${tag} is rejected after ${history.published.at(-1) ?? "no release"}, since it skips or repeats a release`, () => {
    const { status, stdout, stderr } = tagVersion(tag, history);
    assert.equal(status, 1);
    assert.equal(stdout, "");
    assert.match(stderr, new RegExp(`the next release is ${next}$`, "m"));
  });
}

// CI reads the project's <Version> for the tag's own major, so the major check alone can't catch a mistyped major.
test("a tag of the wrong major is rejected when it isn't that major's next release", () => {
  const { status, stdout, stderr } = tagVersion("v18.2.1", { ...released("17.2.0"), project: "18.0.0" });
  assert.equal(status, 1);
  assert.equal(stdout, "");
  assert.match(stderr, /'18\.2\.1' doesn't follow any release of Umbraco major 18: the next release is 18\.0\.0/);
});

test("a tag that never published isn't a release, but its number can't be used again", () => {
  // v17.3.0 was tagged, but its build failed or its deployment was rejected, so nuget.org never got it.
  const history = { tags: ["v17.2.0", "v17.3.0"], published: ["17.2.0"] };

  assert.equal(tagVersion("v17.3.1", history).status, 0);
  assert.equal(tagVersion("v17.4.0", history).status, 0);
  assert.equal(tagVersion("v17.2.1", history).status, 0);
  const skipped = tagVersion("v17.5.0", history);
  assert.equal(skipped.status, 1);
  assert.match(skipped.stderr, /the last release of Umbraco major 17, 17\.2\.0: the next release is 17\.2\.1 or 17\.3\.0 or 17\.3\.1 or 17\.4\.0$/m);
});

test("a stray stable tag that never published doesn't block the major's pre-releases", () => {
  const { status, stdout, stderr } = tagVersion("v19.0.0-rc.2", { tags: ["v19.0.0-rc.1", "v19.0.0"], project: "19.0.0", umbraco: "19.0.0-rc2" });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "19.0.0-rc.2\n");
});

test("a stable tag is rejected while the major's Umbraco version is a pre-release", () => {
  const { status, stdout, stderr } = tagVersion("v19.0.0", { project: "19.0.0", umbraco: "19.0.0-rc2" });
  assert.equal(status, 1);
  assert.equal(stdout, "");
  assert.match(stderr, /'19\.0\.0' is a stable version, but Umbraco 19's lowest supported version is the pre-release 19\.0\.0-rc2/);
});

test("a pre-release tag builds while the major's Umbraco version is a pre-release", () => {
  const { status, stderr } = tagVersion("v19.0.0-rc.1", { project: "19.0.0", umbraco: "19.0.0-rc2" });
  assert.equal(status, 0, stderr);
});

test("the baseline is the major's last published release, not counting the tag being built", () => {
  const dir = repo(["v0.8.0", "v17.1.0", "v17.2.0", "v17.3.0", "v17.4.0-beta.1", "v18.0.0"]);
  const published = ["0.8.0", "17.1.0", "17.2.0", "17.4.0-beta.1", "18.0.0"];
  const baseline = (github, project) => run(dir, github, { project, published, baseline: true }).stdout;

  // v17.3.0 is tagged but never published, so it isn't a baseline.
  assert.equal(baseline(branchBuild(), "17.0.0"), "17.2.0\n");
  assert.equal(baseline(tagBuild("v17.2.0"), "17.0.0"), "17.1.0\n");
  assert.equal(baseline(tagBuild("v18.0.0"), "18.0.0"), "\n");
  assert.equal(baseline(branchBuild(), "19.0.0"), "\n");
});

test("a branch build is the patch after the highest stable tag or release of the project's major, reachable or not", () => {
  const dir = repo(["v17.8.0"]);
  git(dir, "switch", "--quiet", "-c", "main");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Release");
  git(dir, "tag", "v17.10.0");
  git(dir, "tag", "v17.11.0-beta.1");
  git(dir, "switch", "--quiet", "develop");

  const { status, stdout, stderr } = run(dir, branchBuild("42"), { published: ["17.8.0", "17.10.0"] });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.10.1-ci.42\n");
});

test("a branch build counts a tag that never published, since its number is used", () => {
  const { stdout } = run(repo(["v17.2.0", "v17.3.0"]), branchBuild(), { published: ["17.2.0"] });
  assert.equal(stdout, "17.3.1-ci.7\n");
});

test("tags and releases of other majors don't count", () => {
  const dir = repo(["v0.8.0", "v16.4.0", "v17.1.0", "v18.0.0"]);

  const { status, stdout, stderr } = run(dir, branchBuild(), { published: ["0.8.0", "16.4.0", "17.1.0", "18.0.0"] });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.1.1-ci.7\n");
});

test("each Umbraco major builds from its own tags", () => {
  const dir = repo(["v17.3.0", "v18.1.0"]);
  const published = ["17.3.0", "18.1.0"];

  assert.equal(run(dir, branchBuild(), { project: "17.0.0", published }).stdout, "17.3.1-ci.7\n");
  assert.equal(run(dir, branchBuild(), { project: "18.0.0", published }).stdout, "18.1.1-ci.7\n");
  assert.equal(run(dir, branchBuild(), { project: "19.0.0", umbraco: "19.0.0-rc2", published }).stdout, "19.0.0-ci.7\n");
});

test("before the major's first stable release, a build uses the project's <Version>", () => {
  const dir = repo(["v0.8.0", "v17.0.0-beta.1"]);

  const { status, stdout, stderr } = run(dir, branchBuild(), { published: ["0.8.0", "17.0.0-beta.1"] });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.0.0-ci.7\n");
});

test("outside CI, the run number is 0", () => {
  const { status, stdout, stderr } = run(repo(["v17.8.0"]), {}, { published: ["17.8.0"] });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.8.1-ci.0\n");
});

test("with no stable tag and no project version, the build is rejected", () => {
  const { status, stderr } = run(repo(), { GITHUB_RUN_NUMBER: "7" }, { project: "", umbraco: "17.8.2" });
  assert.equal(status, 1);
  assert.match(stderr, /'-ci\.7' is not a valid package version/);
});
