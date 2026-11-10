// Prints the version a CI build packs, from GitHub's ref variables and the repository's release tags.
//
//   node build/package-version.mjs <project file>
//
// A tag v1.2.3 builds 1.2.3. Any other build is {next patch}-ci.{run number}, where the next patch follows the
// highest stable release tag, or is the project's <Version> before the first release. Needs the tags fetched.
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";

const SEMANTIC_VERSION = /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$/;

const [, , projectFile] = process.argv;
const { GITHUB_REF_TYPE, GITHUB_REF_NAME = "", GITHUB_RUN_NUMBER = "0" } = process.env;

const version = GITHUB_REF_TYPE === "tag"
  ? GITHUB_REF_NAME.replace(/^v/, "")
  : `${nextPatch() ?? projectVersion()}-ci.${GITHUB_RUN_NUMBER}`;

if (!SEMANTIC_VERSION.test(version)) {
  console.error(`'${version}' is not a valid package version`);
  process.exit(1);
}
console.log(version);

function nextPatch() {
  const lastRelease = lastStableTag();
  if (!lastRelease) return undefined;
  const [major, minor, patch] = lastRelease.slice(1).split(".").map(Number);
  return `${major}.${minor}.${patch + 1}`;
}

// The highest stable tag in the repository, reachable or not: Gitflow tags releases on main before develop has them.
function lastStableTag() {
  const tags = execFileSync("git", ["tag", "--list", "v*.*.*", "--sort=-v:refname"], { encoding: "utf8" });
  return tags.split("\n").find((tag) => /^v\d+\.\d+\.\d+$/.test(tag.trim()))?.trim();
}

function projectVersion() {
  return readFileSync(projectFile, "utf8").match(/<Version>(.*)<\/Version>/)?.[1] ?? "";
}
