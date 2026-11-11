# Contributing

How the Umbricians team works on Umbricians Live View. Contributions come from the team; whether outside
contributions are accepted is decided when the repository is made public, and this page says so then.

## Branches

The repository uses Gitflow:

- `develop` is the default branch and holds the next release.
- Work happens on a `feature/…` branch cut from `develop`, and goes back to `develop` through a pull request.
- `main` only changes through a `release/x.y.z` or `hotfix/x.y.z` pull request, and every version is tagged there.

Open your pull request against `develop`. Larger changes start as an issue saying what and why, with "Done when"
criteria the pull request is checked against.

## Building and testing

The client builds on Node 26, the version CI uses.

Run the steps the `build` check runs before opening a pull request:

```bash
cd src/Umbricians.LiveView/Client && npm ci && npm test && npm run build && cd -
dotnet build Umbricians.LiveView.slnx -c Release
dotnet test Umbricians.LiveView.slnx -c Release --no-build
node build/smoke-test.mjs demo/Umbricians.LiveView.Demo /App_Plugins/UmbriciansLiveView/umbricians-live-view.js umbricians-live-view
dotnet pack src/Umbricians.LiveView -c Release -o artifacts -p:Version=$(node build/package-version.mjs src/Umbricians.LiveView/Umbricians.LiveView.csproj)
```

In PowerShell, pack with:

```powershell
$version = node build/package-version.mjs src/Umbricians.LiveView/Umbricians.LiveView.csproj
dotnet pack src/Umbricians.LiveView -c Release -o artifacts -p:Version=$version
```

`npm run build` ends by checking the built bundle with `scripts/check-bundle.mjs`, and fails naming each problem: the
bundle entry exporting more than `manifests`, a backoffice module bundled instead of left external, a decorated field
emitted as a class field, or a decorator left uncompiled. It also fails when it has nothing to check: no source maps,
or no decorated fields.

The solution includes the demo site, so `dotnet build` builds it too. Pack with a build, as above: the dashboard
bundle must be built first, and packing stops with an error if it is missing. Package validation compares the package
with the last release, so it is packed with the version CI uses, which comes after it (the script needs the release
tags, which a clone fetches).

Every pack also checks the README it generates for nuget.org, and fails naming each problem: a relative link, a link
off the release's ref, or an HTML image or comment left in. When you change `build/nuget-readme.mjs`, run its
tests too; the `build` check doesn't:

```bash
node --test "build/*.test.mjs"
```

In Git Bash on Windows, the bundle path argument gets rewritten into a Windows path, so run the smoke test there as
`MSYS_NO_PATHCONV=1 node build/smoke-test.mjs …`. PowerShell and CI are unaffected.

## Pull requests

Fill in the template: a summary of what changed, the evidence it works, and the merge danger. Link the issue it
closes with "Closes #N".
