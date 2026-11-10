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

Run the steps the `build` check runs before opening a pull request:

```bash
cd src/Umbricians.LiveView/Client && npm ci && npm test && npm run build && cd -
dotnet build Umbricians.LiveView.slnx -c Release
dotnet test Umbricians.LiveView.slnx -c Release --no-build
node build/smoke-test.mjs demo/Umbricians.LiveView.Demo /App_Plugins/UmbriciansLiveView/umbricians-live-view.js umbricians-live-view
dotnet pack src/Umbricians.LiveView -c Release -o artifacts
```

The solution includes the demo site, so `dotnet build` builds it too. Pack with a build, as above: the dashboard
bundle must be built first, and packing stops with an error if it is missing.

In Git Bash on Windows, the bundle path argument gets rewritten into a Windows path, so run the smoke test there as
`MSYS_NO_PATHCONV=1 node build/smoke-test.mjs …`. PowerShell and CI are unaffected.

## Pull requests

Fill in the template: a summary of what changed, the evidence it works, and the merge danger. Link the issue it
closes with "Closes #N".
