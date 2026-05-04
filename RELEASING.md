# Releasing

This document describes how to release a new version of the PostHog Unity SDK.

## Overview

Releases use [changesets](https://github.com/changesets/changesets) for version management and changelog generation:

1. Add a changeset to your PR describing the change
2. Merge the PR — GitHub Actions handles the rest (no release label required)

## Prerequisites

- [Node.js](https://nodejs.org/) (see `.nvmrc` for version)
- [pnpm](https://pnpm.io/) installed (`npm install -g pnpm`)

Run `pnpm install` to install dependencies.

## Adding a Changeset

When you make a change that should be included in the next release, add a changeset:

```bash
pnpm changeset
```

This will prompt you to:

1. Select the package(s) affected (`com.posthog.unity`)
2. Choose the semver bump type (patch/minor/major)
3. Write a summary of the change

The changeset file is created in `.changeset/` and should be committed with your PR.

### Version Guidelines

Follow [Semantic Versioning](https://semver.org/):

- **patch**: Bug fixes, backwards compatible
- **minor**: New features, backwards compatible
- **major**: Breaking changes

## Release Process

### 1. Create your PR with a changeset

Include a changeset file in your PR (created via `pnpm changeset`).

### 2. Merge the PR

No release label is required. When the PR is merged to `main`, the release workflow:

1. Checks for pending changesets
2. Sends a Slack notification requesting approval
3. On approval:
   - Applies changesets (bumps version, updates CHANGELOG.md)
   - Syncs version to `com.posthog.unity/package.json` and `SdkInfo.Generated.cs`
   - Commits the version bump to `main`
   - Creates a git tag (`vX.Y.Z`)
   - Creates a GitHub Release with auto-generated notes

### Manual trigger

You can also trigger the release workflow manually from the [Actions tab](../../actions/workflows/release.yml) via **Run workflow**.

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

- A PR with a changeset is merged to `main`
- Or manually via workflow_dispatch

### "No changesets found" error

Ensure your PR includes a changeset file in `.changeset/`. Run `pnpm changeset` to create one.

### Need to re-run a failed release

Go to [Actions > Release](../../actions/workflows/release.yml) and click **Run workflow**.
