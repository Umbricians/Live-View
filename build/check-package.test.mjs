// Tests for check-package.mjs, run by the build check with `node --test "build/*.test.mjs"`. Each test builds a small
// package in memory, with the zip layout NuGet writes.
import { test } from "node:test";
import assert from "node:assert/strict";
import { crc32, deflateRawSync } from "node:zlib";
import { checkPackage, readZip, umbracoRange } from "./check-package.mjs";

const BUNDLE = "/App_Plugins/UmbriciansExample/umbricians-example.js";
const expected = { version: "17.2.0", umbracoVersion: "17.8.2", bundlePath: BUNDLE };

// A zip of files ({ path: contents }), deflating the ones named in deflated and storing the rest.
function zip(files, deflated = []) {
  const locals = [];
  const central = [];
  let offset = 0;
  for (const [name, contents] of Object.entries(files)) {
    const raw = Buffer.from(contents);
    const deflate = deflated.includes(name);
    const data = deflate ? deflateRawSync(raw) : raw;
    const nameBytes = Buffer.from(name);
    const fields = (header) => {
      header.writeUInt16LE(deflate ? 8 : 0, header.length === 30 ? 8 : 10);
      return header;
    };
    const local = fields(Buffer.alloc(30));
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt32LE(crc32(raw), 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(raw.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    const entry = fields(Buffer.alloc(46));
    entry.writeUInt32LE(0x02014b50, 0);
    entry.writeUInt32LE(crc32(raw), 16);
    entry.writeUInt32LE(data.length, 20);
    entry.writeUInt32LE(raw.length, 24);
    entry.writeUInt16LE(nameBytes.length, 28);
    entry.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    central.push(entry, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const directory = Buffer.concat(central);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(Object.keys(files).length, 8);
  end.writeUInt16LE(Object.keys(files).length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

function nuspec({ version = "17.2.0", range = "[17.8.2, 18.0.0-a)", dependencies } = {}) {
  dependencies ??= ["Umbraco.Cms.Api.Common", "Umbraco.Cms.Web.Common"].map(
    (id) => `<dependency id="${id}" version="${range}" exclude="Build,Analyzers" />`,
  );
  return `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Umbricians.Example</id>
    <version>${version}</version>
    <dependencies>
      <group targetFramework="net10.0">
        ${dependencies.join("\n        ")}
      </group>
    </dependencies>
  </metadata>
</package>`;
}

// A package's files, as readZip returns them, after a round trip through a zip.
function packageFiles(overrides = {}) {
  const files = {
    "Umbricians.Example.nuspec": nuspec(),
    "README.md": "# Example",
    "icon.png": "png",
    [`staticwebassets${BUNDLE}`]: "export const manifests = [];",
    ...overrides,
  };
  for (const [path, contents] of Object.entries(files)) if (contents === undefined) delete files[path];
  return readZip(zip(files, ["Umbricians.Example.nuspec"]));
}

test("the Umbraco range runs from the lowest version to before the next major's pre-releases", () => {
  assert.equal(umbracoRange("17.8.2"), "[17.8.2, 18.0.0-a)");
  assert.equal(umbracoRange("19.0.0-rc2"), "[19.0.0-rc2, 20.0.0-a)");
});

test("a zip's stored and deflated files are read back", () => {
  const files = readZip(zip({ "a.txt": "stored", "b/c.txt": "deflated ".repeat(20) }, ["b/c.txt"]));
  assert.deepEqual([...files.keys()], ["a.txt", "b/c.txt"]);
  assert.equal(files.get("a.txt").toString(), "stored");
  assert.equal(files.get("b/c.txt").toString(), "deflated ".repeat(20));
});

test("a file that isn't a zip is refused", () => {
  assert.throws(() => readZip(Buffer.from("not a zip")), /not a zip file/);
});

test("a package with the right version, range and files passes", () => {
  assert.deepEqual(checkPackage(packageFiles(), expected), []);
});

test("a package of another version fails", () => {
  assert.deepEqual(checkPackage(packageFiles({ "Umbricians.Example.nuspec": nuspec({ version: "17.2.0-ci.4" }) }), expected), [
    "its version is 17.2.0-ci.4, not 17.2.0",
  ]);
});

// 18.0.0-0 keeps out the 18 pre-releases too, but nuget.org refuses a package with it: a label must start with a letter.
for (const range of ["17.8.2", "[17.8.2, 18.0.0)", "[17.8.2, 18.0.0-0)", "[17.8.2, 19.0.0-a)", "[17.8.0, 18.0.0-a)", "[18.3.2, 19.0.0-a)"]) {
  test(`an Umbraco range of ${range} fails`, () => {
    const problems = checkPackage(packageFiles({ "Umbricians.Example.nuspec": nuspec({ range }) }), expected);
    assert.deepEqual(problems, [
      `Umbraco.Cms.Api.Common is ${range}, not [17.8.2, 18.0.0-a)`,
      `Umbraco.Cms.Web.Common is ${range}, not [17.8.2, 18.0.0-a)`,
    ]);
  });
}

test("one Umbraco dependency with the wrong range fails, whatever the others have", () => {
  const dependencies = [
    `<dependency id="Umbraco.Cms.Api.Common" version="[17.8.2, 18.0.0-a)" />`,
    `<dependency id="Umbraco.Cms.Web.Common" version="17.8.2" />`,
    `<dependency id="Some.Other.Package" version="1.0.0" />`,
  ];
  assert.deepEqual(checkPackage(packageFiles({ "Umbricians.Example.nuspec": nuspec({ dependencies }) }), expected), [
    "Umbraco.Cms.Web.Common is 17.8.2, not [17.8.2, 18.0.0-a)",
  ]);
});

test("a package with no Umbraco dependency fails", () => {
  const dependencies = [`<dependency id="Some.Other.Package" version="1.0.0" />`];
  assert.deepEqual(checkPackage(packageFiles({ "Umbricians.Example.nuspec": nuspec({ dependencies }) }), expected), ["it has no Umbraco dependency"]);
});

test("a package of a release candidate major checks the release candidate's range", () => {
  const files = packageFiles({ "Umbricians.Example.nuspec": nuspec({ version: "19.0.0-rc.1", range: "[19.0.0-rc2, 20.0.0-a)" }) });
  assert.deepEqual(checkPackage(files, { version: "19.0.0-rc.1", umbracoVersion: "19.0.0-rc2", bundlePath: BUNDLE }), []);
});

test("a package missing its README, icon or bundle fails, naming each", () => {
  const files = packageFiles({ "README.md": undefined, "icon.png": undefined, [`staticwebassets${BUNDLE}`]: undefined });
  assert.deepEqual(checkPackage(files, expected), [
    "the README (README.md) is missing",
    "the icon (icon.png) is missing",
    `the dashboard bundle (staticwebassets${BUNDLE}) is missing`,
  ]);
});

test("a package without exactly one .nuspec at the root fails", () => {
  assert.deepEqual(checkPackage(packageFiles({ "Umbricians.Example.nuspec": undefined }), expected), ["expected one .nuspec at the root, found 0"]);
});
