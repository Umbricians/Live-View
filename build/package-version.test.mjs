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

function tagVersion(name) {
  const dir = mkdtempSync(join(tmpdir(), "package-version-"));
  return run(dir, tagBuild(name), join(dir, "Missing.csproj"));
}

test("a tag builds its own version, without reading git or the project file", () => {
  const { status, stdout, stderr } = tagVersion("v1.2.3");
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "1.2.3\n");
  assert.equal(stderr, "");
});

test("a pre-release tag builds its pre-release version", () => {
  const { status, stdout, stderr } = tagVersion("v1.3.0-beta.1");
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "1.3.0-beta.1\n");
});

for (const tag of ["v1.2", "v1.2.3.4", "v1.x.0"]) {
  test(`the tag ${tag} is rejected`, () => {
    const { status, stdout, stderr } = tagVersion(tag);
    const version = tag.slice(1);
    assert.equal(status, 1);
    assert.equal(stdout, "");
    assert.match(stderr, new RegExp(`'${version.replace(/\./g, "\.")}' is not a valid package version`));
  });
}

test("a branch build is the patch after the highest stable tag, reachable or not", () => {
  const dir = repo("0.1.0");
  git(dir, "tag", "v0.8.0");
  git(dir, "switch", "--quiet", "-c", "main");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Release");
  git(dir, "tag", "v0.10.0");
  git(dir, "tag", "v0.11.0-beta.1");
  git(dir, "switch", "--quiet", "develop");

  const { status, stdout, stderr } = run(dir, {
    GITHUB_REF_TYPE: "branch",
    GITHUB_REF_NAME: "develop",
    GITHUB_RUN_NUMBER: "42",
  });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "0.10.1-ci.42\n");
});

test("before the first stable release, a build uses the project's <Version>", () => {
  const dir = repo("0.1.0");
  git(dir, "tag", "v1.0.0-beta.1");

  const { status, stdout, stderr } = run(dir, { GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: "7" });
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "0.1.0-ci.7\n");
});

test("outside CI, the run number is 0", () => {
  const dir = repo("0.1.0");
  git(dir, "tag", "v0.8.0");

  const { status, stdout, stderr } = run(dir, {});
  assert.equal(status, 0, stderr);
  assert.equal(stdout, "0.8.1-ci.0\n");
});

test("with no stable tag and no <Version>, the build is rejected", () => {
  const dir = repo();

  const { status, stderr } = run(dir, { GITHUB_RUN_NUMBER: "7" });
  assert.equal(status, 1);
  assert.match(stderr, /'-ci\.7' is not a valid package version/);
});
