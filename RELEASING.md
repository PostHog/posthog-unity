# Releasing

This document describes how to release a new version of the PostHog Unity SDK.

## Overview

Releases follow a PR-based workflow:

1. Run `bin/release` to create a release PR
2. Review and merge the PR
3. GitHub Actions automatically creates the tag and release

## Prerequisites

- Write access to the repository
- [GitHub CLI](https://cli.github.com/) installed and authenticated (`gh auth login`)
- [jq](https://jqlang.github.io/jq/) installed (`brew install jq`)

Run `bin/bootstrap` to install dependencies.

## Release Process

### 1. Create a release PR

```bash
bin/release
```

The script will:

1. Prompt for the bump type (patch, minor, or major)
2. Create a `release/vX.Y.Z` branch from `main`
3. Update `package.json` and generate `SdkInfo.Generated.cs`
4. Push and create a PR

You can also specify the bump type directly:

```bash
bin/release patch  # 0.1.0 -> 0.1.1
bin/release minor  # 0.1.0 -> 0.2.0
bin/release major  # 0.1.0 -> 1.0.0
```

### 2. Review and merge

Review the PR to ensure:

- Version bump is correct
- CI checks pass
- Ready to release

Merge the PR when ready.

### 3. Automatic release

When the PR is merged, GitHub Actions automatically:

- Creates tag `vX.Y.Z`
- Creates a GitHub Release with auto-generated notes

## Version Guidelines

Follow [Semantic Versioning](https://semver.org/):

- **PATCH** (0.0.1): Bug fixes, backwards compatible
- **MINOR** (0.1.0): New features, backwards compatible
- **MAJOR** (1.0.0): Breaking changes

Pre-release versions can use suffixes like `1.0.0-preview.1`.

## Canceling a Release

To cancel a release before merging:

```bash
gh pr close release/vX.Y.Z --delete-branch
```

## Manual Release (Exceptional Cases)

If you need to trigger a release manually (e.g., re-running a failed release):

1. Go to [Actions > Release](../../actions/workflows/release.yml)
2. Click **Run workflow**
3. Enter the version (must match `package.json`)
4. Optionally enable **Dry run** to preview

## Version Pinning for Users

Users can install specific versions via git URL:

```text
# Latest
https://github.com/PostHog/posthog-unity.git?path=com.posthog.unity

# Specific version
https://github.com/PostHog/posthog-unity.git?path=com.posthog.unity#v0.1.0
```

## Troubleshooting

### Release workflow didn't trigger

The workflow only triggers when:

- A PR is merged (not closed without merging)
- The PR branch starts with `release/v`

Check the PR was from the correct branch name.

### "Tag already exists" error

The tag was already created. Either:

- Use a different version number
- Delete the existing tag if it was created in error:

  ```bash
  git tag -d v0.1.0
  git push origin :refs/tags/v0.1.0
  ```

### Need to update a release PR

If you need to make changes to a release PR before merging:

```bash
git checkout release/vX.Y.Z
# Make changes
git add .
git commit -m "Fix release"
git push
```
