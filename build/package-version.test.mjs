// Tests for package-version.mjs, run by the build check with `node --test "build/*.test.mjs"`. The version rules are
// tested through packageVersion with the tags and nuget.org versions each test gives it, the nuget.org lookup with a
// fake fetch, and reading tags in a git repo of its own in a temp folder.
import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { main, packageVersion, publishedVersions, repositoryTags } from "./package-version.mjs";

const tagBuild = (name, runAttempt = "1") => ({ type: "tag", name, runNumber: "42", runAttempt });
const branchBuild = (runNumber = "7") => ({ type: "branch", name: "develop", runNumber });

// Each version is published and tagged, as a release normally is.
const released = (...versions) => ({ tags: versions.map((version) => `v${version}`), releases: versions });

// The version for ref, for a project whose <Version> is project (CI passes the major being built, so 18.0.0 for a
// v18 tag) and whose lowest Umbraco version is umbraco. A tag build's own tag is in the repository too.
function version(ref, { tags = [], commitTags, releases = [], project = "17.0.0", umbraco = `${project.split(".")[0]}.8.2`, baseline = false } = {}) {
  const allTags = ref.type === "tag" && !tags.includes(ref.name) ? [...tags, ref.name] : tags;
  commitTags ??= ref.type === "tag" ? [ref.name] : [];
  return packageVersion({ project, umbracoVersion: umbraco, tags: allTags, commitTags, releases, ref, baseline });
}

const rejects = (ref, options, message) => assert.throws(() => version(ref, options), { message });

test("a tag builds its own version", () => {
  assert.equal(version(tagBuild("v17.2.3"), released("17.2.2")), "17.2.3");
});

test("a pre-release tag builds its pre-release version", () => {
  assert.equal(version(tagBuild("v17.3.0-beta.1"), released("17.2.0")), "17.3.0-beta.1");
});

for (const tag of ["v17.2", "v17.2.3.4", "v17.x.0"]) {
  test(`the tag ${tag} is rejected`, () => {
    rejects(tagBuild(tag), {}, `'${tag.slice(1)}' is not a valid package version`);
  });
}

for (const tag of ["v0.9.0", "v18.0.0", "v1.0.0-beta.1"]) {
  test(`the tag ${tag} is rejected, since its major isn't the project's Umbraco major`, () => {
    rejects(tagBuild(tag), {}, /doesn't start with the Umbraco major 17 that the project's <Version> \(17\.0\.0\) supports/);
  });
}

for (const [tag, history] of [
  ["v17.0.0", released()],
  ["v17.2.1", released("17.1.0", "17.2.0")],
  ["v17.3.0", released("17.2.0", "17.2.1")],
  ["v17.3.0-rc.1", released("17.2.0")],
]) {
  test(`the tag ${tag} follows ${history.releases.at(-1) ?? "no release"} of its major`, () => {
    assert.equal(version(tagBuild(tag), history), tag.slice(1));
  });
}

for (const [tag, history, next] of [
  ["v17.1.0", released(), "17.0.0"],
  ["v17.2.2", released("17.2.0"), "17.2.1 or 17.3.0"],
  ["v17.4.0", released("17.2.0"), "17.2.1 or 17.3.0"],
  ["v17.2.0-beta.1", released("17.2.0"), "17.2.1 or 17.3.0"],
  ["v17.1.1", released("17.1.0", "17.2.0"), "17.2.1 or 17.3.0"],
]) {
  test(`the tag ${tag} is rejected after ${history.releases.at(-1) ?? "no release"}, since it skips or repeats a release`, () => {
    rejects(tagBuild(tag), history, new RegExp(`the next release is ${next}$`));
  });
}

// CI reads the project's <Version> for the tag's own major, so the major check alone can't catch a mistyped major.
test("a tag of the wrong major is rejected when it isn't that major's next release", () => {
  rejects(tagBuild("v18.2.1"), { ...released("17.2.0"), project: "18.0.0" }, "'18.2.1' doesn't follow any release of Umbraco major 18: the next release is 18.0.0");
});

test("a tag that never published isn't a release, but its number can't be used again", () => {
  // v17.3.0 was tagged, but its build failed or its deployment was rejected, so nuget.org never got it.
  const history = { tags: ["v17.2.0", "v17.3.0"], releases: ["17.2.0"] };

  assert.equal(version(tagBuild("v17.3.1"), history), "17.3.1");
  assert.equal(version(tagBuild("v17.4.0"), history), "17.4.0");
  assert.equal(version(tagBuild("v17.2.1"), history), "17.2.1");
  rejects(tagBuild("v17.5.0"), history, "'17.5.0' doesn't follow the last release of Umbraco major 17, 17.2.0: the next release is 17.2.1 or 17.3.0 or 17.3.1 or 17.4.0");
});

test("a tag the rule refused doesn't open the way to the numbers after it", () => {
  // v17.4.0 skipped 17.3.0, so its build failed, but tags are never moved, so it stays.
  const history = { tags: ["v17.2.0", "v17.4.0"], releases: ["17.2.0"] };

  for (const skipped of ["v17.4.1", "v17.5.0"]) {
    rejects(tagBuild(skipped), history, /the next release is 17\.2\.1 or 17\.3\.0$/);
  }
  assert.equal(version(tagBuild("v17.3.0"), history), "17.3.0");
});

test("unpublished tags the rule allowed open the way one after another", () => {
  // v17.3.0 and then v17.3.1 were allowed but never published, so each uses its number up.
  const history = { tags: ["v17.2.0", "v17.3.0", "v17.3.1"], releases: ["17.2.0"] };
  assert.equal(version(tagBuild("v17.3.2"), history), "17.3.2");
  assert.equal(version(tagBuild("v17.4.0"), history), "17.4.0");
});

test("a tag for a version nuget.org already has is refused", () => {
  // Say v17.3.0's release job failed after the push, and the tag was deleted and pushed again on another commit.
  rejects(tagBuild("v17.3.0"), released("17.2.0", "17.3.0"), "'17.3.0' is already on nuget.org, and a version is never reused: tag the next release instead");
  rejects(tagBuild("v17.4.0-beta.1"), { ...released("17.3.0"), releases: ["17.3.0", "17.4.0-beta.1"] }, /'17\.4\.0-beta\.1' is already on nuget\.org/);
});

test("a re-run of the run that published a version builds it again", () => {
  assert.equal(version(tagBuild("v17.3.0", "2"), released("17.2.0", "17.3.0")), "17.3.0");
});

test("a stable tag on a commit that already carries a release of its major is refused", () => {
  // The release pull request wasn't merged, so the new tag went on the previous release's merge commit.
  const history = { ...released("17.2.0", "18.0.0"), commitTags: ["v17.2.0", "v18.0.0", "v17.3.0"] };
  rejects(tagBuild("v17.3.0"), history, "The commit tagged v17.3.0 is already released as v17.2.0: merge the release pull request and tag its merge commit");
});

test("a tag that never published doesn't block the next number on the same commit", () => {
  // v17.4.0 went on the release merge commit by mistake and was refused; the right tag goes on the same commit.
  const refused = { ...released("17.2.0"), tags: ["v17.2.0", "v17.4.0"], commitTags: ["v17.4.0", "v17.3.0"] };
  assert.equal(version(tagBuild("v17.3.0"), refused), "17.3.0");

  // v17.3.0's build failed or its deployment was rejected, so the next number goes on its commit.
  const failed = { ...released("17.2.0"), tags: ["v17.2.0", "v17.3.0"], commitTags: ["v17.3.0", "v17.3.1"] };
  assert.equal(version(tagBuild("v17.3.1"), failed), "17.3.1");
});

test("one commit can carry a release of each major", () => {
  const history = { ...released("17.2.0", "18.0.0"), commitTags: ["v17.3.0", "v18.1.0"] };
  assert.equal(version(tagBuild("v17.3.0"), history), "17.3.0");
  assert.equal(version(tagBuild("v18.1.0"), { ...history, project: "18.0.0" }), "18.1.0");
});

test("a pre-release tag can sit beside a stable one", () => {
  const history = { ...released("17.2.0"), commitTags: ["v17.2.0", "v17.3.0-beta.1"] };
  assert.equal(version(tagBuild("v17.3.0-beta.1"), history), "17.3.0-beta.1");
});

test("a stray stable tag that never published doesn't block the major's pre-releases", () => {
  const options = { tags: ["v19.0.0-rc.1", "v19.0.0"], project: "19.0.0", umbraco: "19.0.0-rc2" };
  assert.equal(version(tagBuild("v19.0.0-rc.2"), options), "19.0.0-rc.2");
});

test("a stable tag is rejected while the major's Umbraco version is a pre-release", () => {
  rejects(tagBuild("v19.0.0"), { project: "19.0.0", umbraco: "19.0.0-rc2" }, /'19\.0\.0' is a stable version, but Umbraco 19's lowest supported version is the pre-release 19\.0\.0-rc2/);
});

test("a pre-release tag builds while the major's Umbraco version is a pre-release", () => {
  assert.equal(version(tagBuild("v19.0.0-rc.1"), { project: "19.0.0", umbraco: "19.0.0-rc2" }), "19.0.0-rc.1");
});

test("the baseline is the major's last published release, not counting the tag being built", () => {
  const history = {
    tags: ["v0.8.0", "v17.1.0", "v17.2.0", "v17.3.0", "v17.4.0-beta.1", "v18.0.0"],
    releases: ["0.8.0", "17.1.0", "17.2.0", "17.4.0-beta.1", "18.0.0"],
    baseline: true,
  };

  // v17.3.0 is tagged but never published, so it isn't a baseline.
  assert.equal(version(branchBuild(), history), "17.2.0");
  assert.equal(version(tagBuild("v17.2.0"), history), "17.1.0");
  assert.equal(version(tagBuild("v18.0.0"), { ...history, project: "18.0.0" }), "");
  assert.equal(version(branchBuild(), { ...history, project: "19.0.0" }), "");
});

test("a branch build is the patch after the highest stable tag or release of the project's major", () => {
  const history = { tags: ["v17.8.0", "v17.10.0", "v17.11.0-beta.1"], releases: ["17.8.0", "17.10.0", "17.11.0-beta.1"] };
  assert.equal(version(branchBuild("42"), history), "17.10.1-ci.42");
});

test("a branch build counts a tag that never published, since its number is used", () => {
  assert.equal(version(branchBuild(), { tags: ["v17.2.0", "v17.3.0"], releases: ["17.2.0"] }), "17.3.1-ci.7");
});

test("tags and releases of other majors don't count", () => {
  assert.equal(version(branchBuild(), released("0.8.0", "16.4.0", "17.1.0", "18.0.0")), "17.1.1-ci.7");
});

test("each Umbraco major builds from its own tags", () => {
  const history = released("17.3.0", "18.1.0");

  assert.equal(version(branchBuild(), { ...history, project: "17.0.0" }), "17.3.1-ci.7");
  assert.equal(version(branchBuild(), { ...history, project: "18.0.0" }), "18.1.1-ci.7");
  assert.equal(version(branchBuild(), { ...history, project: "19.0.0", umbraco: "19.0.0-rc2" }), "19.0.0-ci.7");
});

test("before the major's first stable release, a build uses the project's <Version>", () => {
  assert.equal(version(branchBuild(), released("0.8.0", "17.0.0-beta.1")), "17.0.0-ci.7");
});

test("outside CI, the run number is 0", () => {
  assert.equal(version({}, released("17.8.0")), "17.8.1-ci.0");
});

test("with no stable tag and no project version, the build is rejected", () => {
  rejects(branchBuild(), { project: "", umbraco: "17.8.2" }, "'-ci.7' is not a valid package version");
});

// A fetch that records the URL it was asked for and answers with status and body, or throws error.
function fakeFetch({ status = 200, body, error } = {}) {
  const fake = async (url) => {
    fake.url = url;
    if (error) throw error;
    return { status, ok: status >= 200 && status < 300, json: async () => body };
  };
  return fake;
}

test("the lookup asks nuget.org's flat container for the lowercased package id", async () => {
  const fetch = fakeFetch({ body: { versions: ["17.0.0"] } });
  await publishedVersions("Umbricians.LiveView", { fetch, tags: ["v0.8.0"] });
  assert.equal(fetch.url, "https://api.nuget.org/v3-flatcontainer/umbricians.liveview/index.json");
});

test("the lookup returns every version, stable and pre-release, and the version rules pick the stable ones", async () => {
  const releases = await publishedVersions("Umbricians.LiveView", {
    fetch: fakeFetch({ body: { versions: ["0.8.0", "17.0.0", "17.1.0-beta.1", "17.0.1"] } }),
    tags: ["v0.8.0"],
  });
  assert.deepEqual(releases, ["0.8.0", "17.0.0", "17.1.0-beta.1", "17.0.1"]);
  assert.equal(version(branchBuild(), { releases, baseline: true }), "17.0.1");
});

test("a package nuget.org doesn't know has no releases before the repository's first release tag", async () => {
  const fetch = fakeFetch({ status: 404 });
  assert.deepEqual(await publishedVersions("Umbricians.New", { fetch, tags: [] }), []);
  // Pre-release tags aren't releases, and neither is the first release's own tag while it builds.
  assert.deepEqual(await publishedVersions("Umbricians.New", { fetch, tags: ["v17.0.0-rc.1"] }), []);
  assert.deepEqual(await publishedVersions("Umbricians.New", { fetch, tags: ["v17.0.0-rc.1", "v17.0.0"], building: "v17.0.0" }), []);
});

test("a package nuget.org doesn't know fails once the repository has release tags, since the lookup must be wrong", async () => {
  await assert.rejects(publishedVersions("Umbricians.Typo", { fetch: fakeFetch({ status: 404 }), tags: ["v0.8.0"] }), {
    message: /doesn't know Umbricians\.Typo, but the repository has release tags: check the package id/,
  });
});

test("a failed lookup fails the build", async () => {
  await assert.rejects(publishedVersions("Umbricians.LiveView", { fetch: fakeFetch({ status: 500 }), tags: ["v0.8.0"] }), {
    message: /Couldn't read the published versions from .*: 500/,
  });
  await assert.rejects(publishedVersions("Umbricians.LiveView", { fetch: fakeFetch({ error: new Error("timed out") }), tags: [] }), {
    message: /Couldn't read the published versions from .*: timed out/,
  });
  await assert.rejects(publishedVersions("Umbricians.LiveView", { fetch: fakeFetch({ body: {} }), tags: ["v0.8.0"] }), {
    message: /didn't list any versions/,
  });
});

// main is what the build runs: the command-line arguments and GitHub's variables, the lookup and the tags together.
const cli = (env, { tags = ["v0.8.0", "v17.2.0"], commitTags, fetch = fakeFetch({ body: { versions: ["0.8.0", "17.2.0"] } }), args = [] } = {}) =>
  main(["Umbricians.LiveView", "17.0.0", "17.8.2", ...args], env, { fetch, tags, commitTags: commitTags ?? (env.GITHUB_REF_NAME ? [env.GITHUB_REF_NAME] : []) });

test("a tag build through main packs the tag's version, not a -ci one", async () => {
  const env = { GITHUB_REF_TYPE: "tag", GITHUB_REF_NAME: "v17.3.0", GITHUB_RUN_NUMBER: "42" };
  assert.equal(await cli(env, { tags: ["v0.8.0", "v17.2.0", "v17.3.0"] }), "17.3.0");
  await assert.rejects(cli({ ...env, GITHUB_REF_NAME: "v17.5.0" }, { tags: ["v17.2.0", "v17.5.0"] }), { message: /the next release is 17\.2\.1 or 17\.3\.0/ });
});

test("main reads the run attempt and the commit's tags", async () => {
  const env = { GITHUB_REF_TYPE: "tag", GITHUB_REF_NAME: "v17.2.0", GITHUB_RUN_NUMBER: "42" };
  const history = { tags: ["v17.1.0", "v17.2.0"], fetch: fakeFetch({ body: { versions: ["17.1.0", "17.2.0"] } }) };
  await assert.rejects(cli(env, history), { message: /'17\.2\.0' is already on nuget\.org/ });
  assert.equal(await cli({ ...env, GITHUB_RUN_ATTEMPT: "2" }, history), "17.2.0");

  const next = { ...env, GITHUB_REF_NAME: "v17.3.0" };
  await assert.rejects(cli(next, { commitTags: ["v17.2.0", "v17.3.0"] }), { message: /already released as v17\.2\.0/ });
});

test("a branch build through main packs the next patch with its run number", async () => {
  assert.equal(await cli({ GITHUB_REF_TYPE: "branch", GITHUB_REF_NAME: "develop", GITHUB_RUN_NUMBER: "42" }), "17.2.1-ci.42");
  assert.equal(await cli({}), "17.2.1-ci.0");
});

test("the baseline through main is the major's last published release", async () => {
  assert.equal(await cli({ GITHUB_REF_TYPE: "branch" }, { args: ["--baseline"] }), "17.2.0");
});

test("main fails on a 404 once the repository has a stable tag other than the one being built", async () => {
  const fetch = fakeFetch({ status: 404 });
  await assert.rejects(cli({ GITHUB_REF_TYPE: "branch" }, { fetch }), { message: /check the package id/ });
  await assert.rejects(cli({ GITHUB_REF_TYPE: "tag", GITHUB_REF_NAME: "v17.0.0" }, { fetch, tags: ["v0.8.0", "v17.0.0"] }), { message: /check the package id/ });
  // A new package's first release: its own tag is the only stable one, so nuget.org not knowing it is expected.
  assert.equal(await cli({ GITHUB_REF_TYPE: "tag", GITHUB_REF_NAME: "v17.0.0" }, { fetch, tags: ["v17.0.0"] }), "17.0.0");
});

test("main without its arguments fails with the usage", async () => {
  await assert.rejects(main(["Umbricians.LiveView"], {}, { fetch: fakeFetch(), tags: [] }), { message: /^Usage: node build\/package-version\.mjs/ });
});

test("every tag in the repository is read, reachable from the branch or not", (t) => {
  const git = (dir, ...args) =>
    execFileSync("git", ["-c", "user.name=Test", "-c", "user.email=test@example.test", "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false", ...args], {
      cwd: dir,
      stdio: "pipe",
    });
  const dir = mkdtempSync(join(tmpdir(), "package-version-"));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  git(dir, "init", "--quiet", "--initial-branch=develop");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Start");
  git(dir, "tag", "v17.8.0");
  git(dir, "switch", "--quiet", "-c", "main");
  git(dir, "commit", "--quiet", "--allow-empty", "-m", "Release");
  git(dir, "tag", "v17.10.0");
  git(dir, "switch", "--quiet", "develop");

  assert.deepEqual(repositoryTags(dir).sort(), ["v17.10.0", "v17.8.0"]);
  assert.deepEqual(repositoryTags(dir, "HEAD"), ["v17.8.0"]);
  assert.deepEqual(repositoryTags(dir, "main"), ["v17.10.0"]);
});
