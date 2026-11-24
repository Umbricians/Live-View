// Prints the version a CI build packs, or the release its pack checks the public API against, from GitHub's ref
// variables and the repository's release tags.
//
//   node build/package-version.mjs <project version>             the version
//   node build/package-version.mjs <project version> --baseline  the major's last release, or nothing before its first
//
// The package's major is the Umbraco major it supports. The project's <Version> for the major being built carries it
// (17.0.0 for Umbraco 17); CI reads it with `dotnet msbuild -getProperty:Version -p:UmbracoMajor=17`.
//
// A tag v17.2.3 builds 17.2.3. It fails unless it is the next release of its major: the next patch or minor after the
// major's last stable release tag, or <major>.0.0 before its first. A pre-release (v17.3.0-beta.1) must lead up to
// such a release. So a mistyped major or a skipped number never publishes. Any other build is
// {next patch}-ci.{run number}, where the next patch follows the major's last stable release tag, or is the
// project's <Version> before that major's first release. Needs the tags fetched.
import { execFileSync } from "node:child_process";

const SEMANTIC_VERSION = /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$/;

const [, , projectVersionArgument = "", mode] = process.argv;
const { GITHUB_REF_TYPE, GITHUB_REF_NAME = "", GITHUB_RUN_NUMBER = "0" } = process.env;

const project = projectVersionArgument.trim();
const umbracoMajor = project.split(".")[0];
const tagBuild = GITHUB_REF_TYPE === "tag";

if (mode === "--baseline") {
  console.log(lastStableTag()?.slice(1) ?? "");
  process.exit(0);
}

const version = tagBuild
  ? GITHUB_REF_NAME.replace(/^v/, "")
  : `${nextPatch() ?? project}-ci.${GITHUB_RUN_NUMBER}`;

if (!SEMANTIC_VERSION.test(version)) {
  fail(`'${version}' is not a valid package version`);
}
if (version.split(".")[0] !== umbracoMajor) {
  fail(`'${version}' doesn't start with the Umbraco major ${umbracoMajor} that the project's <Version> (${project}) supports`);
}
if (tagBuild) {
  const release = version.split("-")[0];
  const allowed = nextReleases();
  if (!allowed.includes(release)) {
    const last = lastStableTag();
    fail(
      `'${version}' doesn't follow ${last ? `the last release of Umbraco major ${umbracoMajor}, ${last}` : `any release of Umbraco major ${umbracoMajor}`}: ` +
        `the next release is ${allowed.join(" or ")}`,
    );
  }
}
console.log(version);

function fail(message) {
  console.error(message);
  process.exit(1);
}

function nextPatch() {
  const last = lastStableTag();
  if (!last) return undefined;
  const [major, minor, patch] = last.slice(1).split(".").map(Number);
  return `${major}.${minor}.${patch + 1}`;
}

// The releases a tag may build: the next patch or minor after the major's last release, or its first.
function nextReleases() {
  const last = lastStableTag();
  if (!last) return [`${umbracoMajor}.0.0`];
  const [major, minor, patch] = last.slice(1).split(".").map(Number);
  return [`${major}.${minor}.${patch + 1}`, `${major}.${minor + 1}.0`];
}

// The highest stable tag of the project's major in the repository, reachable or not: Gitflow tags releases on main
// before develop has them. Tags of other Umbraco majors don't count, and neither does the tag being built.
function lastStableTag() {
  const tags = execFileSync("git", ["tag", "--list", `v${umbracoMajor}.*.*`, "--sort=-v:refname"], { encoding: "utf8" });
  return tags
    .split("\n")
    .map((tag) => tag.trim())
    .find((tag) => /^v\d+\.\d+\.\d+$/.test(tag) && !(tagBuild && tag === GITHUB_REF_NAME));
}
