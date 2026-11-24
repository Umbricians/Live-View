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
// major's last release, or <major>.0.0 before the major's first release, or the same after a later tag that was
// itself a next release but never published. A tag the rule refused doesn't count, so it can't open the way to a
// skipped number. A pre-release (v17.3.0-beta.1) must lead up to such a release. So a skipped number fails, and so
// does a mistyped major, unless the version it names happens to be that major's next release. A tag build also fails:
// - for a version nuget.org already has, unless the run is a re-run (GITHUB_RUN_ATTEMPT above 1);
// - for a stable tag on a commit that already carries a release of the same major (a tag nuget.org has), such as
//   the previous release's merge commit when the release pull request wasn't merged first. A tag that never
//   published doesn't count, so the next number can still go on its commit;
// - for a stable tag while the major's Umbraco version is a pre-release, since a stable package can't depend on an
//   Umbraco release candidate.
//
// Any other build is {next patch}-ci.{run number}, where the next patch follows the highest stable release or tag of
// the project's major, or is the project's <Version> before the major has either. Needs the tags fetched.
//
// The functions are exported for build/package-version.test.mjs; running the file calls main with the real fetch and
// the repository's tags, and prints what it returns.
import { execFileSync } from "node:child_process";
import { pathToFileURL } from "node:url";

const SEMANTIC_VERSION = /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$/;
const STABLE = /^\d+\.\d+\.\d+$/;
const STABLE_TAG = /^v\d+\.\d+\.\d+$/;

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  try {
    console.log(await main(process.argv.slice(2), process.env, { fetch, tags: repositoryTags(), commitTags: repositoryTags(undefined, "HEAD") }));
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}

/**
 * What the script prints for its command-line arguments and GitHub's ref variables in env, looking the package up
 * with fetch. tags are the repository's tags, and commitTags the tags on the commit being built.
 */
export async function main([packageId, project, umbracoVersion, mode], env, { fetch, tags, commitTags = [] }) {
  if (!packageId || project === undefined || !umbracoVersion) {
    throw new Error("Usage: node build/package-version.mjs <package id> <project version> <Umbraco version> [--baseline]");
  }
  const ref = {
    type: env.GITHUB_REF_TYPE,
    name: env.GITHUB_REF_NAME ?? "",
    runNumber: env.GITHUB_RUN_NUMBER ?? "0",
    runAttempt: env.GITHUB_RUN_ATTEMPT ?? "1",
  };
  const building = ref.type === "tag" ? ref.name : undefined;
  const releases = await publishedVersions(packageId, { fetch, tags, building });
  return packageVersion({ project, umbracoVersion, tags, commitTags, releases, ref, baseline: mode === "--baseline" });
}

/**
 * The version a build packs, or with baseline the major's last published release ("" before its first). tags are
 * the repository's tag names, commitTags the tags on the commit being built, releases the versions nuget.org has,
 * and ref GitHub's ref type, name, run number and run attempt. Throws with the reason when the version isn't one the
 * build may pack.
 */
export function packageVersion({ project, umbracoVersion, tags, commitTags = [], releases, ref = {}, baseline = false }) {
  project = project.trim();
  const umbracoMajor = project.split(".")[0];
  const tagBuild = ref.type === "tag";
  const tagVersion = tagBuild ? ref.name.replace(/^v/, "") : undefined;
  const ofThisMajor = (version) => version.split(".")[0] === umbracoMajor;

  // The major's stable releases and stable tags, other than the version being built.
  const majorReleases = releases.filter((version) => STABLE.test(version) && ofThisMajor(version) && version !== tagVersion).sort(compare);
  const lastRelease = majorReleases.at(-1);
  const majorTags = tags
    .filter((tag) => STABLE_TAG.test(tag) && !(tagBuild && tag === ref.name))
    .map((tag) => tag.slice(1))
    .filter(ofThisMajor);

  if (baseline) return lastRelease ?? "";

  const highest = [...majorReleases, ...majorTags].sort(compare).at(-1);
  const version = tagBuild ? tagVersion : `${highest ? following(highest)[0] : project}-ci.${ref.runNumber ?? "0"}`;

  if (!SEMANTIC_VERSION.test(version)) {
    throw new Error(`'${version}' is not a valid package version`);
  }
  if (version.split(".")[0] !== umbracoMajor) {
    throw new Error(`'${version}' doesn't start with the Umbraco major ${umbracoMajor} that the project's <Version> (${project}) supports`);
  }
  if (tagBuild) {
    if (releases.includes(version) && Number(ref.runAttempt ?? "1") <= 1) {
      throw new Error(`'${version}' is already on nuget.org, and a version is never reused: tag the next release instead`);
    }
    // Only tags nuget.org has: a refused or failed tag left on the commit mustn't block the next number going there.
    const released = commitTags.filter(
      (tag) => STABLE_TAG.test(tag) && tag !== ref.name && ofThisMajor(tag.slice(1)) && releases.includes(tag.slice(1)),
    );
    if (STABLE.test(version) && released.length > 0) {
      throw new Error(
        `The commit tagged ${ref.name} is already released as ${released.join(" and ")}: merge the release pull request and tag its merge commit`,
      );
    }
    if (STABLE.test(version) && !STABLE.test(umbracoVersion)) {
      throw new Error(
        `'${version}' is a stable version, but Umbraco ${umbracoMajor}'s lowest supported version is the pre-release ${umbracoVersion}: tag a pre-release such as v${version}-rc.1`,
      );
    }
    const next = nextReleases(lastRelease, majorTags, umbracoMajor);
    if (!next.includes(version.split("-")[0])) {
      const after = lastRelease ? `the last release of Umbraco major ${umbracoMajor}, ${lastRelease}` : `any release of Umbraco major ${umbracoMajor}`;
      throw new Error(`'${version}' doesn't follow ${after}: the next release is ${next.join(" or ")}`);
    }
  }
  return version;
}

/**
 * Every version of the package on nuget.org, listed or not, from its flat container: the versions a restore can
 * download, which is what the baseline check downloads. A package nuget.org doesn't know (404) has no releases, but
 * only before its first release: once the repository has a stable tag other than the one being built (building), a
 * 404 means the lookup is wrong (the package id, say), and carrying on would pack without the API check.
 */
export async function publishedVersions(packageId, { fetch, tags, building }) {
  const repositoryHasReleases = tags.some((tag) => STABLE_TAG.test(tag) && tag !== building);
  const url = `https://api.nuget.org/v3-flatcontainer/${packageId.toLowerCase()}/index.json`;
  let response;
  try {
    response = await fetch(url, { signal: AbortSignal.timeout(30_000) });
  } catch (error) {
    throw new Error(`Couldn't read the published versions from ${url}: ${error.message}`);
  }
  if (response.status === 404) {
    if (repositoryHasReleases) {
      throw new Error(`${url} doesn't know ${packageId}, but the repository has release tags: check the package id`);
    }
    return [];
  }
  if (!response.ok) {
    throw new Error(`Couldn't read the published versions from ${url}: ${response.status}`);
  }
  const { versions } = await response.json();
  if (!Array.isArray(versions)) {
    throw new Error(`${url} didn't list any versions`);
  }
  return versions;
}

/**
 * Every tag in the repository, reachable or not (Gitflow tags releases on main before develop has them), or with
 * commit only the tags on that commit.
 */
export function repositoryTags(cwd, commit) {
  return execFileSync("git", ["tag", "--list", ...(commit ? ["--points-at", commit] : [])], { cwd, encoding: "utf8" })
    .split("\n")
    .map((tag) => tag.trim())
    .filter(Boolean);
}

// The next patch or minor after the last release, or the major's first release. A later tag that was itself one of
// those but never published uses its number up, so the next patch or minor after it is allowed too, working up from
// the last release. A tag the rule refused doesn't count.
function nextReleases(lastRelease, tags, major) {
  const allowed = new Set(lastRelease ? following(lastRelease) : [`${major}.0.0`]);
  for (const tag of [...tags].sort(compare)) {
    if (allowed.has(tag) && (!lastRelease || compare(tag, lastRelease) > 0)) {
      for (const version of following(tag)) allowed.add(version);
    }
  }
  return [...allowed].sort(compare);
}

function following(version) {
  const [major, minor, patch] = version.split(".").map(Number);
  return [`${major}.${minor}.${patch + 1}`, `${major}.${minor + 1}.0`];
}

function compare(a, b) {
  const [x, y] = [a, b].map((version) => version.split(".").map(Number));
  return x[0] - y[0] || x[1] - y[1] || x[2] - y[2];
}
