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

### Failed or cancelled release

The independent [release failure notification workflow](.github/workflows/release-failure-notification.yml) checks every completed main-branch release attempt and alerts if a job failed, timed out, or was cancelled. It also alerts when the run failed but the version bump, commit, and tag job never ran, which happens when GitHub Actions stops mid-run. Once the GitHub release exists, alerts describe follow-up problems rather than claiming the release failed. The workflow still alerts if the GitHub release exists but the released Slack notification was skipped.

Cancelled runs with no jobs are ignored, including superseded queued runs and manual cancellations before any job is created. A version-bump failure before any steps ran is also ignored when the rejection-reply step confirms an explicit rejection in the same attempt and no other job failed.

Job inspection retries transient GitHub API failures up to three times. If inspection still fails, it logs the HTTP status or error type and message, then posts a fallback alert with the overall run status and logs link so the release can be checked manually. Error diagnostics stay in the run log, not in the Slack message.

Alerts reply to the approval thread in the release approvals Slack channel. The release workflow preserves the thread timestamp in a run artifact for 30 days, independently of release approval. Failed-job reruns reuse the latest available thread from the same or an earlier attempt. If the timestamp is missing, invalid, or unavailable, the alert falls back to a new channel message. The completion workflow must be present on the default branch to run.
