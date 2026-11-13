// Starts the built demo site, checks it serves the backoffice, the package's bundle and the package's Swagger
// document, which only exists once the package's composer has run, then stops it.
//
//   node build/smoke-test.mjs <demo project folder> <bundle URL path> <Swagger document name>
//
// Run it after `dotnet build -c Release`. In Development the demo installs itself on a fresh SQLite database.
import { spawn } from "node:child_process";
import { basename, join, resolve } from "node:path";

const SITE_URL = "http://127.0.0.1:5080";
const STARTUP_TIMEOUT_MS = 180_000;
const POLL_INTERVAL_MS = 2_000;

const [, , demoFolder, bundlePath, swaggerDocument] = process.argv;
if (!demoFolder || !bundlePath || !swaggerDocument) {
  console.error("Usage: node smoke-test.mjs <demo project folder> <bundle URL path> <Swagger document name>");
  process.exit(1);
}

const projectDir = resolve(demoFolder);
const assembly = join(projectDir, "bin", "Release", "net10.0", `${basename(projectDir)}.dll`);
const site = spawn("dotnet", [assembly, "--urls", SITE_URL], {
  cwd: projectDir,
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", DOTNET_ENVIRONMENT: "Development" },
  stdio: "inherit",
});

try {
  await waitUntilOk(`${SITE_URL}/umbraco`);
  await expectOk(`${SITE_URL}${bundlePath}`);
  await expectOk(`${SITE_URL}/umbraco/swagger/${swaggerDocument}/swagger.json`);
  console.log("The demo serves the backoffice, the package's bundle and its Swagger document.");
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  site.kill();
}

async function waitUntilOk(url) {
  const deadline = Date.now() + STARTUP_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (site.exitCode !== null) throw new Error(`The demo exited with code ${site.exitCode} before ${url} answered.`);
    if (await isOk(url)) return;
    await new Promise((done) => setTimeout(done, POLL_INTERVAL_MS));
  }
  throw new Error(`${url} didn't answer within ${STARTUP_TIMEOUT_MS / 1000} seconds.`);
}

async function expectOk(url) {
  if (!(await isOk(url))) throw new Error(`${url} didn't return a success status.`);
}

async function isOk(url) {
  try {
    return (await fetch(url)).ok;
  } catch {
    return false; // The site isn't listening yet.
  }
}
