// Writes the package version into umbraco-package.json, which the backoffice shows and uses as its cache buster.
//
//   node build/stamp-version.mjs <umbraco-package.json> <version>
import { readFileSync, writeFileSync } from "node:fs";

const [, , manifestPath, version] = process.argv;
if (!manifestPath || !version) {
  console.error("Usage: node stamp-version.mjs <umbraco-package.json> <version>");
  process.exit(1);
}

const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
manifest.version = version;
writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + "\n");
