# Releasing

For maintainers. The repository uses Gitflow: `develop` holds the next release, and `main` only changes through
release and hotfix pull requests. [`.github/workflows/build.yml`](../.github/workflows/build.yml) builds and packs
every push to `main` and `develop` and every pull request, once for each Umbraco major. A version tag on `main`
publishes to NuGet, and so does a pre-release tag on a `release/*` branch.

The tag sets the package version (`v17.2.3` publishes `17.2.3`, and `v17.3.0-beta.1` a pre-release), and its major
is the Umbraco major the package is built for: a tag builds and publishes that major only, so `v18.2.3` publishes
Live View 18.2.3 for Umbraco 18. A tag of a major [`Directory.Build.props`](../Directory.Build.props) doesn't know
fails the build. Each major numbers its own minors and patches, so releasing the same change for several majors takes
one tag each, such as `v17.3.0` and `v18.1.0` on the same commit. A tag must be the next release of its major: the
next patch or minor after the major's last release (`v17.2.1` or `v17.3.0` after `v17.2.0`), or `vN.0.0` for a
major's first, and a pre-release must lead up to one. Any other tag, such as a mistyped major or a skipped number,
fails the build. Umbraco 19 is a release candidate, so Live View 19 gets pre-release tags only (`v19.0.0-rc.1`) until
Umbraco 19 is out: a stable package can't depend on a pre-release Umbraco, so its pack fails. New features take a
minor version and fixes a patch, and a change that could break a site's code or configuration waits for the next
Umbraco major. Only the package in `src/Umbricians.LiveView` is packed; the demo site
is built and smoke-tested but never published. Once the package is on NuGet, the workflow creates a matching GitHub
release with generated notes and the `.nupkg` and `.snupkg` attached.

## A release

1. Cut the release branch from an up-to-date `develop`:

   ```bash
   git checkout develop && git pull && git checkout -b release/17.2.0
   ```

2. Push it, then open a pull request from `release/17.2.0` into `main` with the `release-flow` label. Only release
   fixes go on this branch.
3. Merge it with a merge commit (not squash) once the `build` check passes.
4. Tag the merge commit on `main` and push the tag, one tag for each Umbraco major the release is for. A major
   that is still on Umbraco release candidates (19 for now) gets no tag here: tag it on the release branch before
   step 3, as [a pre-release](#a-pre-release).

   ```bash
   git checkout main && git pull && git tag -a v17.2.0 -m "v17.2.0"
   ```

   ```bash
   git push origin v17.2.0
   ```

5. In each tag's run, open **Review deployments** and approve `production`.
6. Check each publish log says the package was pushed, not skipped as a duplicate, and that the GitHub release has
   the `.nupkg` attached. nuget.org lists the package a few minutes later, after validation.
7. Merge `main` back into `develop` through a pull request with the `release-flow` label, from a branch cut
   from `main`:

   ```bash
   git checkout main && git pull && git checkout -b back-merge/17.2.0
   ```

   On that branch, delete every `<Suppression>` from the suppressions file of each major you tagged
   (`src/Umbricians.LiveView/CompatibilitySuppressions.17.xml` for a `v17` tag), and leave the other majors' files
   alone: their suppressions still allow differences from their own last release. The pack checks each major
   against its last release tag, so the next release of a tagged major is checked against this one. If the merge
   into `develop` conflicts on a suppressions file, keep any suppressions `develop` added since the release. Merge
   it with a merge commit.

## A hotfix

Cut `hotfix/17.2.1` from `main`, fix it there, and open a pull request into `main` with the `release-flow` label.
Then follow steps 3 to 7 above with the hotfix version.

## A pre-release

To publish a pre-release from a release branch before it merges, tag a commit on it with a suffix and push the tag:

```bash
git checkout release/17.2.0 && git pull && git tag -a v17.2.0-beta.1 -m "v17.2.0-beta.1"
```

```bash
git push origin v17.2.0-beta.1
```

Then follow steps 5 and 6 above. It publishes a NuGet pre-release and a GitHub pre-release, and nothing merges back.

A release tag that isn't on `main`, or a pre-release tag that isn't on a `release/*` branch, fails the build and
never publishes. If a build fails on its tag, fix it through a
hotfix and release the next version number; a version is never reused, and a tag is never moved.

nuget.org keeps the README each version was published with, so README changes reach the package page with the
next release.

## How publishing is authorised

Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so no
API key is stored anywhere. The workflow exchanges GitHub's OIDC token for a NuGet API key that lasts one hour.
nuget.org only accepts that token from this repository, this workflow file and the `production` environment. The
environment only lets `v*` tags deploy and waits for a maintainer's approval, so a branch, a pull request or a fork
can't publish.

## One-off setup

Make the repository public before the first release, unless the Umbricians organization is on GitHub Team or
higher. On GitHub Free a private repository has no rulesets and silently ignores the environment rules below, so
nothing would stop a branch from publishing. The package page also needs it public: the NuGet README's images and
links point at this repository, and the Umbraco Marketplace reads `umbraco-marketplace.json` from it. Check the
GitHub settings below again after making it public.

**On nuget.org**, signed in as a member of the `umbricians` organization, create a trusted publishing policy with
owner `umbricians`, repository owner `Umbricians`, repository `Live-View`, workflow `build.yml` and environment
`production`. Owner and repository are matched without regard to case.

**In the GitHub repository**, every item is required:

1. **Branches:** push `main` and `develop`, then make `develop` the default branch (**Settings → General**).
2. **Settings → Environments:** create an environment named `production`, and in it:
   - add **required reviewers** (the maintainers) and leave **Prevent self-review** off, so a maintainer working
     alone can approve their own release. Turn off **Allow administrators to bypass configured protection rules**,
     so every publish still waits for an approval in the run;
   - under **Deployment branches and tags**, choose **Selected branches and tags** and add one rule of type **Tag**
     for `v*`, with no branch rules;
   - add a secret `NUGET_USER` holding the nuget.org **username** (profile name, not email) of the account that
     **created** the trusted publishing policy. For a policy owned by an organization, that is still the creator's
     own username.
3. **Settings → Rules → Rulesets:**
   - a branch ruleset for `main` and `develop` that requires a pull request and the `build` status check, and
     blocks force pushes and deletion. Allow merge commits on `main`, since release and hotfix branches merge back.
   - a tag ruleset for `v*` that restricts creation, update and deletion to maintainers.
4. **Labels:** create a `release-flow` label for release, hotfix and back-merge pull requests, which
   `.github/release.yml` leaves out of the release notes.
