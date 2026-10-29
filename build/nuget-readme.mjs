// Builds the README shipped in the NuGet package from the repository README, so there is one source to edit.
//
//   node build/nuget-readme.mjs <README.md> <output.md> <repository url> [branch]
//
// nuget.org cannot resolve repository-relative paths and only shows images from trusted domains, so this:
// - keeps everything before "## Development" (the parts for people installing the package);
// - drops the HTML logo, since nuget.org shows the package icon;
// - points relative images at raw.githubusercontent.com and relative links at github.com;
// - adds a footer linking to the repository.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";

const [, , inputPath, outputPath, repositoryUrl, branch = "main"] = process.argv;
if (!inputPath || !outputPath || !repositoryUrl) {
  console.error("Usage: node nuget-readme.mjs <README.md> <output.md> <repository url> [branch]");
  process.exit(1);
}

const repo = repositoryUrl.replace(/\.git$/, "").replace(/\/$/, "");
const repoPath = new URL(repo).pathname.replace(/^\//, ""); // Owner/Name
const rawBase = `https://raw.githubusercontent.com/${repoPath}/${branch}/`;
const blobBase = `${repo}/blob/${branch}/`;

let readme = readFileSync(inputPath, "utf8").replace(/\r\n/g, "\n");

const developmentAt = readme.search(/^## Development\b/m);
if (developmentAt >= 0) readme = readme.slice(0, developmentAt);

readme = readme.replace(/^<img [^>]*>\n+/m, "");

const isRelative = (target) => !/^(?:[a-z][a-z0-9+.-]*:|#|\/)/i.test(target);
readme = readme.replace(/(!?)\[([^\]]*)\]\(([^)\s]+)\)/g, (match, bang, text, target) => {
  if (!isRelative(target)) return match;
  const path = target.replace(/^\.\//, "");
  return `${bang}[${text}](${bang ? rawBase : blobBase}${path})`;
});

readme = `${readme.trimEnd()}\n\n---\n\nSource, the demo site and contributor docs: <${repo}>\n`;

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, readme);
console.log(`Wrote ${outputPath}`);
