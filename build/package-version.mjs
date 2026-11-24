// Prints the version a CI build packs, or the release its pack checks the public API against, from GitHub's ref
// variables, the repository's release tags and the versions nuget.org has published.
//
//   node build/package-version.mjs <package id> <project version> <Umbraco version>             the version
//   node build/package-version.mjs <package id> <project version> <Umbraco version> --baseline  the major's last
//                                                                                                 published release
//
// The package's major is the Umbraco major it supports. The project's <Version> for the major being built carries it
// (17.0.0 for Umbraco 17), and <Umbraco version> is the lowest Umbraco version it supports (UmbracoVersion); CI reads
// both with `dotnet msbuild -getProperty:… -p:UmbracoMajor=17`.
//
// A release is a version nuget.org has. A tag that never got there (its build failed, or its deployment was rejected)
// isn't one, so nothing is checked against it, but its number stays used: versions are never reused.
//
// A tag v17.2.3 builds 17.2.3. It fails unless it is the next release of its major: the next patch or minor after the
// major's last release or after a later tag that never published, or <major>.0.0 before the major's first release. A
// pre-release (v17.3.0-beta.1) must lead up to such a release. So a skipped number fails, and so does a mistyped
// major, unless the version it names happens to be that major's next release. A stable tag also fails while the
// major's Umbraco version is a pre-release, since a stable package can't depend on an Umbraco release candidate.
//
// Any other build is {next patch}-ci.{run number}, where the next patch follows the highest stable release or tag of
// the project's major, or is the project's <Version> before the major has either. Needs the tags fetched.
//
// PACKAGE_VERSIONS, a comma-separated list, stands in for nuget.org's versions in the tests.
import { execFileSync } from "node:child_process";

const SEMANTIC_VERSION = /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$/;
const STABLE = /^\d+\.\d+\.\d+$/;

const [, , packageId = "", projectArgument = "", umbracoArgument = "", mode] = process.argv;
const { GITHUB_REF_TYPE, GITHUB_REF_NAME = "", GITHUB_RUN_NUMBER = "0", PACKAGE_VERSIONS } = process.env;

const project = projectArgument.trim();
const umbracoVersion = umbracoArgument.trim();
const umbracoMajor = project.split(".")[0];
const tagBuild = GITHUB_REF_TYPE === "tag";
const tagVersion = tagBuild ? GITHUB_REF_NAME.replace(/^v/, "") : undefined;

if (!packageId || !umbracoVersion) {
  fail("Usage: node build/package-version.mjs <package id> <project version> <Umbraco version> [--baseline]");
}

const releases = (await publishedVersions()).filter(ofThisMajor).sort(compare);
const lastRelease = releases.at(-1);
// Stable tags of the major, other than the one being built, whether or not they published.
const tags = stableTags().filter(ofThisMajor);

if (mode === "--baseline") {
  console.log(lastRelease ?? "");
  process.exit(0);
}

const version = tagBuild ? tagVersion : `${nextPatch() ?? project}-ci.${GITHUB_RUN_NUMBER}`;

if (!SEMANTIC_VERSION.test(version)) {
  fail(`'${version}' is not a valid package version`);
}
if (version.split(".")[0] !== umbracoMajor) {
  fail(`'${version}' doesn't start with the Umbraco major ${umbracoMajor} that the project's <Version> (${project}) supports`);
}
if (tagBuild) {
  if (STABLE.test(version) && !STABLE.test(umbracoVersion)) {
    fail(`'${version}' is a stable version, but Umbraco ${umbracoMajor}'s lowest supported version is the pre-release ${umbracoVersion}: tag a pre-release such as v${version}-rc.1`);
  }
  const allowed = nextReleases();
  if (!allowed.includes(version.split("-")[0])) {
    const after = lastRelease ? `the last release of Umbraco major ${umbracoMajor}, ${lastRelease}` : `any release of Umbraco major ${umbracoMajor}`;
    fail(`'${version}' doesn't follow ${after}: the next release is ${allowed.join(" or ")}`);
  }
}
console.log(version);

function fail(message) {
  console.error(message);
  process.exit(1);
}

function nextPatch() {
  const highest = [...releases, ...tags].sort(compare).at(-1);
  return highest ? following(highest)[0] : undefined;
}

// The releases a tag may build: the next patch or minor after the major's last release, or its first release, and
// the same after any later tag that never published, whose number can't be used again.
function nextReleases() {
  const allowed = lastRelease ? following(lastRelease) : [`${umbracoMajor}.0.0`];
  for (const tag of tags) {
    if (!lastRelease || compare(tag, lastRelease) > 0) allowed.push(...following(tag));
  }
  return [...new Set(allowed)].sort(compare);
}

function following(version) {
  const [major, minor, patch] = version.split(".").map(Number);
  return [`${major}.${minor}.${patch + 1}`, `${major}.${minor + 1}.0`];
}

function ofThisMajor(version) {
  return version.split(".")[0] === umbracoMajor;
}

function compare(a, b) {
  const [x, y] = [a, b].map((version) => version.split(".").map(Number));
  return x[0] - y[0] || x[1] - y[1] || x[2] - y[2];
}

// Every tag in the repository, reachable or not: Gitflow tags releases on main before develop has them.
function stableTags() {
  const output = execFileSync("git", ["tag", "--list", "v*.*.*"], { encoding: "utf8" });
  return output
    .split("\n")
    .map((tag) => tag.trim())
    .filter((tag) => tag !== GITHUB_REF_NAME || !tagBuild)
    .map((tag) => tag.slice(1))
    .filter((version) => STABLE.test(version));
}

// The stable versions nuget.org has, other than the one being built. The flat container lists every version a
// restore can download, listed or not, which is what the baseline check downloads.
async function publishedVersions() {
  let versions;
  if (PACKAGE_VERSIONS !== undefined) {
    versions = PACKAGE_VERSIONS.split(",").map((version) => version.trim()).filter(Boolean);
  } else {
    const url = `https://api.nuget.org/v3-flatcontainer/${packageId.toLowerCase()}/index.json`;
    let response;
    try {
      response = await fetch(url, { signal: AbortSignal.timeout(30_000) });
    } catch (error) {
      fail(`Couldn't read the published versions from ${url}: ${error.message}`);
    }
    if (response.status === 404) {
      versions = [];
    } else if (!response.ok) {
      fail(`Couldn't read the published versions from ${url}: ${response.status}`);
    } else {
      versions = (await response.json()).versions;
    }
  }
  return versions.filter((version) => STABLE.test(version) && version !== tagVersion);
}
