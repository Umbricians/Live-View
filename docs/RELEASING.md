# Releasing

For maintainers. [`.github/workflows/build.yml`](../.github/workflows/build.yml) builds and packs on every push and
pull request. Pushing a version tag publishes to NuGet:

```bash
git tag v0.1.1
```

```bash
git push origin v0.1.1
```

The tag sets the package version (`v1.2.3` publishes `1.2.3`, and `v1.3.0-beta.1` a pre-release). Only the plugin
in `src/Umbricians.LiveView` is packed; the demo site is built as a check but never published. Once the package is
on NuGet, the workflow creates a matching GitHub release with generated release notes and the `.nupkg` attached.

nuget.org keeps the README a version was published with, so README changes reach the package page with the next
release.

## How publishing is authorised

Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so no
API key is stored anywhere. The workflow exchanges GitHub's OIDC token for a NuGet API key that lasts one hour.
nuget.org only accepts that token from this repository, this workflow file and the `production` environment, so
forks cannot publish, and pull requests run without access to secrets.

## One-off setup

In the GitHub repository:

1. **Settings → Environments**: create an environment named `production`.
2. In that environment, add a secret `NUGET_USER` holding the nuget.org **username** (profile name, not email) of
   the account that **created** the trusted publishing policy. For a policy owned by an organization, that is still
   the creator's own username, not the organization's.

On nuget.org, the trusted publishing policy must name owner `umbricians`, repository `live-view`, workflow
`build.yml` and environment `production`. Owner and repository are matched without regard to case.

## Recommended protections

Pushing a version tag is all it takes to publish, so:

- Add **required reviewers** to the `production` environment, so each publish waits for approval.
- Add a **tag ruleset** for `v*` (**Settings → Rules → Rulesets**), so only maintainers can create release tags.
