# Contributing

How the Umbricians team works on Umbricians Live View. Contributions come from the team; whether outside
contributions are accepted is decided when the repository is made public, and this page says so then.

## Branches

The repository uses Gitflow:

- `develop` is the default branch and holds the next release.
- Work happens on a `feature/…` branch cut from `develop`, and goes back to `develop` through a pull request.
- `main` only changes through a `release/x.y.z` or `hotfix/x.y.z` pull request, and every version is tagged there.
- After a release, a `back-merge/x.y.z` branch cut from `main` carries it back into `develop` through a pull
  request, and clears the suppressions of the released majors
  ([RELEASING.md](docs/RELEASING.md#a-release), step 7).

Open your pull request against `develop`. Larger changes start as an issue saying what and why, with "Done when"
criteria the pull request is checked against.

## Building and testing

The client builds on Node 26, the version CI uses. The solution needs the .NET 11 SDK (`global.json`) and the
.NET 10 runtime, which Umbraco 17 and 18 run on.

Live View is built once per Umbraco major, and each build is its own package: Live View 17.x for Umbraco 17, 18.x
for 18 and 19.x for 19. `UmbracoMajor` picks the major, 17 when it is not set, and
[`Directory.Build.props`](Directory.Build.props) maps it to the target framework, the lowest Umbraco version the
package supports and the demo's Umbraco version. Code that differs between majors goes behind `#if UMBRACO_17`,
`UMBRACO_18_OR_GREATER` or `UMBRACO_19_OR_GREATER`.

The `build` check runs these steps for every major. Run them for each major your change could affect before opening
a pull request, with the major's target framework (`net10.0` for 17 and 18, `net11.0` for 19) and API document path
(`/umbraco/swagger/umbricians-live-view/swagger.json` on 17, `/umbraco/openapi/umbricians-live-view.json` from 18):

```bash
major=18
node --test "build/*.test.mjs"
cd src/Umbricians.LiveView/Client && npm ci && npm install --no-save @umbraco-cms/backoffice@$(dotnet msbuild .. -getProperty:UmbracoVersion -p:UmbracoMajor=$major) && npm test && npm run build && cd -
dotnet build Umbricians.LiveView.slnx -c Release -p:UmbracoMajor=$major
dotnet test Umbricians.LiveView.slnx -c Release --no-build -p:UmbracoMajor=$major
node build/smoke-test.mjs demo/Umbricians.LiveView.Demo net10.0 /App_Plugins/UmbriciansLiveView/umbricians-live-view.js /umbraco/openapi/umbricians-live-view.json "Umbricians Live View"
project=$(dotnet msbuild src/Umbricians.LiveView -getProperty:Version -p:UmbracoMajor=$major)
dotnet pack src/Umbricians.LiveView -c Release -o artifacts -p:UmbracoMajor=$major -p:Version=$(node build/package-version.mjs $project) -p:LastReleaseVersion=$(node build/package-version.mjs $project --baseline)
```

In PowerShell, pack with:

```powershell
$major = 18
$project = dotnet msbuild src/Umbricians.LiveView -getProperty:Version -p:UmbracoMajor=$major
$version = node build/package-version.mjs $project
$baseline = node build/package-version.mjs $project --baseline
dotnet pack src/Umbricians.LiveView -c Release -o artifacts -p:UmbracoMajor=$major -p:Version=$version -p:LastReleaseVersion=$baseline
```

`npm install --no-save` swaps in the backoffice types of the major without changing `package-lock.json`; run `npm ci`
again to go back to the Umbraco 17 types it locks.

The smoke test starts the demo on its SQLite database, which is upgraded to the Umbraco major it last ran on, so move
`demo/Umbricians.LiveView.Demo/umbraco/Data` aside before running it on an older major.

`npm run build` ends by checking the built bundle with `scripts/check-bundle.mjs`, and fails naming each problem: the
bundle entry exporting more than `manifests`, the API entry that `@umbricians/live-view` maps to missing or not
exporting what `src/index.ts` does, a backoffice module bundled instead of left external, a decorated field emitted as
a class field, or a decorator left uncompiled. It also fails when it has nothing to check: no source maps,
or no decorated fields. `npm test` runs its own tests, `scripts/check-bundle.test.mjs`, against small folders of
hand-written output.

The solution includes the demo site, so `dotnet build` builds it too. Pack with a build, as above: the dashboard
bundle must be built first, and packing stops with an error if it is missing. Package validation compares the package
with the last release of the same Umbraco major, so it is packed with the version CI uses, which comes after it, and
with that release as `LastReleaseVersion` (the script needs the release tags, which a clone fetches). Differences a
change is allowed to make go in that major's `CompatibilitySuppressions.<major>.xml`.

Every pack checks the README it generates for nuget.org, and fails naming each problem: a relative link, a link
off the release's ref, or an HTML image or comment left in. The `build` check runs the build scripts' tests,
`node --test "build/*.test.mjs"`, before it works out the version: `build/nuget-readme.test.mjs` covers the README
check and `build/package-version.test.mjs` covers the version a build packs, including the version each tag
publishes. The version tests need git on the path.

In Git Bash on Windows, the URL path arguments get rewritten into Windows paths, so run the smoke test there as
`MSYS_NO_PATHCONV=1 node build/smoke-test.mjs …`. PowerShell and CI are unaffected.

## Pull requests

Fill in the template: a summary of what changed, the evidence it works, and the merge danger. Link the issue it
closes with "Closes #N".
