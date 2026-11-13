// Tests for nuget-readme.mjs: run with `node --test "build/*.test.mjs"`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const script = fileURLToPath(new URL("nuget-readme.mjs", import.meta.url));
const repo = "https://github.com/Owner/Repo";

const goodReadme = `<img src="assets/logo.svg" alt="Logo" width="96">

# Package

<!-- A note for people editing the README. -->

![Screenshot](docs/images/screenshot.png)

<img src="./docs/images/chart.png" alt="Chart">

See [getting started](docs/getting-started.md), [the licence](./LICENSE), [below](#install) and
[NuGet](https://www.nuget.org/packages/Owner.Repo).

## Install

\`\`\`html
<img src="kept/as/written.png"> [kept](kept/as/written.md) <!-- kept -->
\`\`\`

## Development

[Contributing](CONTRIBUTING.md) is left out of the package.
`;

function run(readme, ref) {
  const dir = mkdtempSync(join(tmpdir(), "nuget-readme-"));
  const input = join(dir, "README.md");
  const output = join(dir, "out", "README.md");
  writeFileSync(input, readme);
  const result = spawnSync(process.execPath, [script, input, output, repo, ...(ref ? [ref] : [])], { encoding: "utf8" });
  return { ...result, readme: result.status === 0 ? readFileSync(output, "utf8") : undefined };
}

for (const ref of ["main", "v1.2.3"]) {
  test(`a README passes on ${ref}, with its images and links on ${ref}`, () => {
    const { status, stderr, readme } = run(goodReadme, ref);

    assert.equal(status, 0, stderr);
    assert.match(readme, new RegExp(`!\\[Screenshot\\]\\(https://raw\\.githubusercontent\\.com/Owner/Repo/${ref}/docs/images/screenshot\\.png\\)`));
    assert.match(readme, new RegExp(`!\\[Chart\\]\\(https://raw\\.githubusercontent\\.com/Owner/Repo/${ref}/docs/images/chart\\.png\\)`));
    assert.match(readme, new RegExp(`\\(${repo}/blob/${ref}/docs/getting-started\\.md\\)`));
    assert.match(readme, new RegExp(`\\(${repo}/blob/${ref}/LICENSE\\)`));
    assert.match(readme, /\[below\]\(#install\)/);
    assert.match(readme, /<img src="kept\/as\/written\.png"> \[kept\]\(kept\/as\/written\.md\) <!-- kept -->/);
    assert.doesNotMatch(readme, /Logo|A note for people|Contributing/);
  });
}

test("the ref defaults to main", () => {
  const { status, readme } = run(goodReadme);

  assert.equal(status, 0);
  assert.match(readme, /raw\.githubusercontent\.com\/Owner\/Repo\/main\/docs\/images\/screenshot\.png/);
});

const broken = [
  ["a link with a title stays relative", `See [docs](docs/a.md "Docs").\n`, /relative link or image: "docs\/a\.md"/],
  ["an image tag over two lines is left", `<p>x</p>\n\n<img\n  src="a.png">\n`, /<img left in the text/],
  ["an unclosed comment is left", `Text\n\n<!-- never closed\n`, /<!-- left in the text/],
  [
    "an image is written on another ref",
    `![x](https://raw.githubusercontent.com/Owner/Repo/main/a.png)\n`,
    /not on v1\.2\.3: https:\/\/raw\.githubusercontent\.com\/Owner\/Repo\/main\/a\.png/,
  ],
  ["a link is written on another ref", `[x](${repo}/blob/main/a.md)\n`, /not on v1\.2\.3: https:\/\/github\.com\/Owner\/Repo\/blob\/main\/a\.md/],
];

for (const [name, readme, problem] of broken) {
  test(`fails when ${name}`, () => {
    const { status, stderr } = run(`# Package\n\n${readme}`, "v1.2.3");

    assert.equal(status, 1);
    assert.match(stderr, /would show a broken README on nuget\.org/);
    assert.match(stderr, problem);
  });
}
