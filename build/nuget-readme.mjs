// Builds the README shipped in the NuGet package from the repository README, so there is one source to edit.
//
//   node build/nuget-readme.mjs <README.md> <output.md> <repository url> [git ref]
//
// nuget.org cannot resolve repository-relative paths, renders no HTML and only shows images from trusted domains,
// so this:
// - keeps everything before "## Development" (the parts for people installing the package);
// - drops the leading HTML logo, since nuget.org shows the package icon, and HTML comments;
// - turns other HTML images into Markdown images;
// - points relative images at raw.githubusercontent.com and relative links at github.com, on the release's ref;
// - adds a footer linking to the repository.
//
// It then checks what it wrote, and fails naming each problem when a link into the repository is not on the ref, a
// link or image is still relative, or an HTML image or comment is left, so a broken package page fails the pack.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";

const [, , inputPath, outputPath, repositoryUrl, ref = "main"] = process.argv;
if (!inputPath || !outputPath || !repositoryUrl) {
  console.error("Usage: node nuget-readme.mjs <README.md> <output.md> <repository url> [git ref]");
  process.exit(1);
}

const repo = repositoryUrl.replace(/\.git$/, "").replace(/\/$/, "");
const repoPath = new URL(repo).pathname.replace(/^\//, ""); // Owner/Name
const rawBase = `https://raw.githubusercontent.com/${repoPath}/${ref}/`;
const blobBase = `${repo}/blob/${ref}/`;

let readme = readFileSync(inputPath, "utf8").replace(/\r\n/g, "\n");

const developmentAt = readme.search(/^## Development\b/m);
if (developmentAt >= 0) readme = readme.slice(0, developmentAt);

readme = readme.replace(/^\s*<img [^>]*>\n+/, "");

// Fenced code blocks sit at the odd indexes and are kept exactly as written.
const parts = readme.split(/(^```[^\n]*\n[\s\S]*?^```$)/m);
readme = parts.map((part, index) => (index % 2 ? part : rewriteProse(part))).join("");
readme = `${readme.trimEnd()}\n\n---\n\nSource, the demo site and contributor docs: <${repo}>\n`;

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, readme);
console.log(`Wrote ${outputPath}`);

const problems = check(readme);
if (problems.length) {
  console.error(`${outputPath} would show a broken README on nuget.org:\n${problems.map((p) => `- ${p}`).join("\n")}`);
  process.exit(1);
}

function check(text) {
  const problems = [];
  for (const base of [`https://raw.githubusercontent.com/${repoPath}/`, `${repo}/blob/`]) {
    for (let at = text.indexOf(base); at >= 0; at = text.indexOf(base, at + 1)) {
      if (!text.startsWith(`${ref}/`, at + base.length)) {
        problems.push(`not on ${ref}: ${text.slice(at).match(/^[^\s)>"]*/)[0]}`);
      }
    }
  }
  // Code blocks are left as written, so only the prose between them is checked.
  const prose = text.split(/^```[^\n]*\n[\s\S]*?^```$/m).join("\n");
  for (const [, target] of prose.matchAll(/\]\(\s*([^)\s]*)/g)) {
    if (!/^(?:[a-z][a-z0-9+.-]*:|#)/i.test(target)) problems.push(`relative link or image: "${target}"`);
  }
  for (const html of ["<img", "<!--"]) {
    if (prose.includes(html)) problems.push(`${html} left in the text`);
  }
  return problems;
}

function rewriteProse(text) {
  return text
    .replace(/<!--[\s\S]*?-->/g, "")
    .replace(/<img [^>]*>/g, toMarkdownImage)
    .replace(/(!?)\[([^\]]*)\]\(([^)\s]+)\)/g, toRepositoryLink)
    .replace(/\n{3,}/g, "\n\n");
}

function toRepositoryLink(match, bang, text, target) {
  if (/^(?:[a-z][a-z0-9+.-]*:|#|\/)/i.test(target)) return match;
  const path = target.replace(/^\.\//, "");
  return `${bang}[${text}](${bang ? rawBase : blobBase}${path})`;
}

function toMarkdownImage(tag) {
  const attribute = (name) => tag.match(new RegExp(`\\b${name}="([^"]*)"`))?.[1] ?? "";
  return `![${attribute("alt")}](${attribute("src")})`;
}
