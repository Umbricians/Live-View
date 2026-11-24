// Prints the options the release job passes to `gh release create` for a tag, one per line, from the repository's
// stable GitHub releases.
//
//   gh release list --exclude-pre-releases --limit 100 --json tagName | node build/release-options.mjs <tag>
//
// - A tag with a pre-release suffix (v17.3.0-beta.1) becomes a GitHub pre-release.
// - Notes start at the last stable release of the same Umbraco major, so a release after betas lists everything
//   since then, and one tagged beside another major's release isn't empty.
// - Only a stable release of the newest Umbraco major becomes the repository's Latest, so a 17.x hotfix after 18.x
//   doesn't take it. Pre-releases never do.
import { readFileSync } from "node:fs";

const [, , tag] = process.argv;
if (!tag) {
  console.error("Usage: gh release list --exclude-pre-releases --json tagName | node build/release-options.mjs <tag>");
  process.exit(1);
}

// No input means `gh release list` failed, not that there are no releases ([] is what an empty list looks like), and
// guessing would mark an older major's release as Latest.
const input = readFileSync(0, "utf8").trim();
if (!input) {
  console.error("No releases were given on standard input: pass the output of `gh release list --json tagName`.");
  process.exit(1);
}
const stable = JSON.parse(input)
  .map((release) => release.tagName)
  .filter((name) => /^v\d+\.\d+\.\d+$/.test(name) && name !== tag);
const major = majorOf(tag);
const preRelease = tag.includes("-");

const options = [];
if (preRelease) options.push("--prerelease");

const previous = stable.filter((name) => majorOf(name) === major).sort(compare).at(-1);
if (previous) options.push(`--notes-start-tag=${previous}`);

if (!preRelease) {
  const newest = Math.max(0, ...stable.map(majorOf));
  options.push(major >= newest ? "--latest" : "--latest=false");
}

console.log(options.join("\n"));

function majorOf(name) {
  return Number(name.replace(/^v/, "").split(".")[0]);
}

function compare(a, b) {
  const [x, y] = [a, b].map((name) => name.replace(/^v/, "").split(".").map(Number));
  return x[0] - y[0] || x[1] - y[1] || x[2] - y[2];
}
