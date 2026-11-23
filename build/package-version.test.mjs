// Tests for package-version.mjs, run by the build check with `node --test "build/*.test.mjs"`. Each test runs the
// script in a temp folder, with GitHub's ref variables set by the test and a git repo of its own where it needs tags.
import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const script = fileURLToPath(new URL("package-version.mjs", import.meta.url));

// Runs the script in dir. CI's own GITHUB_* variables are dropped so they can't leak in (a tag build sets them).
function run(dir, github, projectFile = join(dir, "Project.csproj")) {
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !/^GITHUB_/i.test(key)));
  const { status, stdout, stderr } = spawnSync(process.execPath, [script, projectFile], {
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

// A git repo on develop with one empty commit and a project file with the given <Version> (none when omitted).
function repo(version) {
  const dir = mkdtempSync(join(tmpdir(), "package-version-"));
  const project = version === undefined
    ? "<Project />"
    : `<Project><PropertyGroup><Version>${version}</Version></PropertyGroup></Project>`;
  writeFileSync(join(dir, "Project.csproj"), project);
  git(dir, "init", "--quiet", "--initial-branch=develop");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Start");
  return dir;
}

const tagBuild = (name) => ({ GITHUB_REF_TYPE: "tag", GITHUB_REF_NAME: name, GITHUB_RUN_NUMBER: "42" });

// A tag build of a project for Umbraco 17. It doesn't read git, so the folder needs no repo.
function tagVersion(name) {
  const dir = mkdtempSync(join(tmpdir(), "package-version-"));
  writeFileSync(join(dir, "Project.csproj"), "<Project><PropertyGroup><Version>17.0.0</Version></PropertyGroup></Project>");
  return run(dir, tagBuild(name));
}

test("a tag builds its own version, without reading git", () => {
  const { status, stdout, stderr } = tagVersion("v17.2.3");
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.2.3\n");
  assert.equal(stderr, "");
});

test("a pre-release tag builds its pre-release version", () => {
  const { status, stdout, stderr } = tagVersion("v17.3.0-beta.1");
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

test("a branch build is the patch after the highest stable tag of the project's major, reachable or not", () => {
  const dir = repo("17.0.0");
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
  const dir = repo("17.0.0");
  git(dir, "tag", "v0.8.0");
  git(dir, "tag", "v16.4.0");
  git(dir, "tag", "v17.1.0");
  git(dir, "tag", "v18.0.0");

  const { status, stdout, stderr } = run(dir, { GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: "7" });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.1.1-ci.7\n");
});

test("before the major's first stable release, a build uses the project's <Version>", () => {
  const dir = repo("17.0.0");
  git(dir, "tag", "v0.8.0");
  git(dir, "tag", "v17.0.0-beta.1");

  const { status, stdout, stderr } = run(dir, { GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: "7" });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.0.0-ci.7\n");
});

test("outside CI, the run number is 0", () => {
  const dir = repo("17.0.0");
  git(dir, "tag", "v17.8.0");

  const { status, stdout, stderr } = run(dir, {});
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "17.8.1-ci.0\n");
});

test("with no stable tag and no <Version>, the build is rejected", () => {
  const dir = repo();

  const { status, stderr } = run(dir, { GITHUB_RUN_NUMBER: "7" });
  assert.equal(status, 1);
  assert.match(stderr, /'-ci\.7' is not a valid package version/);
});
