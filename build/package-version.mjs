// Prints the version a CI build packs, from GitHub's ref variables and the repository's release tags.
//
//   node build/package-version.mjs <project version>
//
// The package's major is the Umbraco major it supports. The project's <Version> for the major being built carries it
// (17.0.0 for Umbraco 17); CI reads it with `dotnet msbuild -getProperty:Version -p:UmbracoMajor=17`.
// A tag v17.2.3 builds 17.2.3, and fails when its major isn't the project's. Any other build is
// {next patch}-ci.{run number}, where the next patch follows the highest stable release tag of the project's major,
// or is the project's <Version> before that major's first release. Needs the tags fetched.
import { execFileSync } from "node:child_process";

const SEMANTIC_VERSION = /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$/;

const [, , projectVersionArgument = ""] = process.argv;
const { GITHUB_REF_TYPE, GITHUB_REF_NAME = "", GITHUB_RUN_NUMBER = "0" } = process.env;

const project = projectVersionArgument.trim();
const umbracoMajor = project.split(".")[0];

const version = GITHUB_REF_TYPE === "tag"
  ? GITHUB_REF_NAME.replace(/^v/, "")
  : `${nextPatch() ?? project}-ci.${GITHUB_RUN_NUMBER}`;

if (!SEMANTIC_VERSION.test(version)) {
  console.error(`'${version}' is not a valid package version`);
  process.exit(1);
}
if (version.split(".")[0] !== umbracoMajor) {
  console.error(`'${version}' doesn't start with the Umbraco major ${umbracoMajor} that the project's <Version> (${project}) supports`);
  process.exit(1);
}
console.log(version);

function nextPatch() {
  const lastRelease = lastStableTag();
  if (!lastRelease) return undefined;
  const [major, minor, patch] = lastRelease.slice(1).split(".").map(Number);
  return `${major}.${minor}.${patch + 1}`;
}

// The highest stable tag of the project's major in the repository, reachable or not: Gitflow tags releases on main
// before develop has them. Tags of an older Umbraco major don't count once the project targets a newer one.
function lastStableTag() {
  const tags = execFileSync("git", ["tag", "--list", `v${umbracoMajor}.*.*`, "--sort=-v:refname"], { encoding: "utf8" });
  return tags.split("\n").find((tag) => /^v\d+\.\d+\.\d+$/.test(tag.trim()))?.trim();
}
