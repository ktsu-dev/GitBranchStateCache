# ktsu.GitBranchStateCache

> Answers "for these paths, which of these branches carry changes I do not have, and what content do they carry" once, centrally, for many clients over a wide-area link.

[![License](https://img.shields.io/github/license/ktsu-dev/GitBranchStateCache.svg?label=License&logo=nuget)](LICENSE.md)
[![NuGet Version](https://img.shields.io/nuget/v/ktsu.GitBranchStateCache?label=Stable&logo=nuget)](https://nuget.org/packages/ktsu.GitBranchStateCache)
[![NuGet Version](https://img.shields.io/nuget/vpre/ktsu.GitBranchStateCache?label=Latest&logo=nuget)](https://nuget.org/packages/ktsu.GitBranchStateCache)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ktsu.GitBranchStateCache?label=Downloads&logo=nuget)](https://nuget.org/packages/ktsu.GitBranchStateCache)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/GitBranchStateCache?label=Commits&logo=github)](https://github.com/ktsu-dev/GitBranchStateCache/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/GitBranchStateCache?label=Contributors&logo=github)](https://github.com/ktsu-dev/GitBranchStateCache/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/GitBranchStateCache/dotnet.yml?branch=main&label=Build&logo=github)](https://github.com/ktsu-dev/GitBranchStateCache/actions)

## Introduction

An Unreal Engine editor with source control enabled asks the same question every thirty seconds: which assets have changed on the branches I care about, so that I can warn someone before they lock one. Answering it locally costs a `git fetch` plus a `git log` and a `git diff` per monitored branch, on every workstation, on a fixed heartbeat. That is worst for the people on residential links, and it is the same work repeated once per open editor.

`ktsu.GitBranchStateCache` does that work once, adjacent to the forge, and serves the result. It keeps a bare, blobless mirror of each repository it is configured for, computes diffs between a client's pushed base and each branch tip, and returns **blob ids per path per branch**. The client compares those against its own working tree, which is local, exact and cheap.

It is not a version control system and not a git server. It never serves object content, never accepts writes, and is never a source of truth for anything.

## Features

- **Git over HTTPS and nothing else.** No forge REST APIs, so there is no per-forge adapter at all and nothing behaves differently against GitHub than against Azure DevOps. GitHub's compare endpoint caps at 300 changed files and truncates silently, which for this question is indistinguishable from a complete answer.
- **Blob ids, not a verdict.** Comparing blob ids catches the changed-then-reverted case exactly, where the plugin's current intersection of a log and a diff only approximates it. For an asset tracked by Git LFS, comparing pointer blobs is comparing LFS object ids, so no LFS awareness is needed.
- **A bare, blobless mirror.** `--filter=blob:none` fetches commits and trees but no file content, and a raw diff reports blob ids rather than blob contents, so it never triggers a lazy fetch. `GIT_NO_LAZY_FETCH=1` is set on every invocation so a mistake surfaces as an error rather than an enormous fetch.
- **A diff cache keyed on the merge base.** Every artist sits on a different commit but a team shares an integration point, so one computed diff per branch serves all of them.
- **No service credentials, ever.** Every upstream operation runs under a requesting client's own credential, leader-pays and coalesced. A background poller was rejected precisely because it would need one.
- **`git ls-remote` is the whole authorization mechanism.** Forge-agnostic, one cheap round trip, and it proves exactly the read access being requested. Nothing is served to a caller whose own credential has not been proven, including when the forge is unreachable.
- **Repository allow-listing, required.** One request for an unlisted repository would be a permanent mirror clone of it, so there is deliberately no pattern meaning "everything" and every pattern must name a literal path segment.
- **Answers that cannot tear.** Branch tips are read once and every later step names those object ids explicitly, so a fetch landing mid-request cannot produce an answer split between two versions of a branch.
- **One binary, two shapes.** The same build is the `gitbranchstatecache` dotnet tool and the container image, so they cannot drift.

## Installation

### As a dotnet tool

```bash
dotnet tool install --global ktsu.GitBranchStateCache.Tool
```

### As a container

```bash
docker pull ghcr.io/ktsu-dev/gitbranchstatecache:latest
```

The image carries the git binary, which the service shells out to for everything it does.

## Usage

### Running locally

```bash
gitbranchstatecache --port 8080 \
  --mirror-root /var/lib/gitbranchstatecache \
  --upstream github=https://github.com \
  --allow github=studio/game.git
```

Or point it at a configuration file and pass nothing else:

```bash
gitbranchstatecache --config /etc/gitbranchstatecache.json
```

`--upstream` is repeatable, and the name becomes the path segment clients address after the version:

```bash
gitbranchstatecache --upstream github=https://github.com --allow github=studio/game.git \
                    --upstream ado=https://dev.azure.com/myorg --allow ado=myproject/_git/game
```

`--allow` is required at least once per upstream and is also repeatable. Unlike `ktsu.GitLfsCache`, there is no pattern meaning every repository: every pattern must name at least one literal path segment. One request for a repository not on the list would clone a permanent mirror of it onto a shared volume, sized by the repository rather than by the request, that nothing ever evicts.

### Asking for branch state

```http
POST /v1/{upstream}/{repositoryPath}/state
Authorization: Basic ...
```

```json
{
  "base": "a1b2c3...",
  "branchPatterns": ["origin/main", "origin/release/*"],
  "paths": ["Content/Maps/Foo.umap", "Content/Chars/Bar.uasset"]
}
```

`base` is the client's latest **pushed** ancestor, not its true HEAD, which `git rev-parse @{upstream}` produces locally without touching the network. The mirror cannot compute a merge base against a commit it has never seen, and an unpushed commit can only make the client more current than the base it declares.

`paths` is optional; omitting it returns every changed path, which is what a client warming its whole state wants and also how a response becomes enormous.

```json
{
  "base": "a1b2c3...",
  "branches": [
    { "name": "origin/main", "tip": "d4e5f6...", "mergeBase": "a1b2c3...", "error": null }
  ],
  "paths": {
    "Content/Chars/Bar.uasset": [
      { "branch": "origin/main", "blob": "9f8e7d...", "status": "M" }
    ]
  },
  "refsAsOf": "2026-08-19T09:47:00Z",
  "partial": false,
  "truncated": false
}
```

A path absent from `paths` is unchanged on every queried branch relative to its merge base. `status` carries git's raw status letter, so a delete (`blob` null) is distinguishable from a modification.

`partial` is true when at least one branch could not be answered for; that branch carries an `error` and is never reported as having nothing changed on it. `refsAsOf` says when the refs behind the answer were last known to match the upstream, so a client can see when a fetch did not succeed.

`409 unknown-base` means the mirror does not hold the commit the client named. The body carries the current branch tips, and the client should fall back to its own local computation for that cycle.

### Listing branches

```http
GET /v1/{upstream}/{repositoryPath}/branches?pattern=origin/release/*
```

Resolves wildcard patterns against the mirror's refs and returns names with tip ids. This removes the other reason a client currently needs a network fetch on its heartbeat. Wildcards here cross path separators, matching `git branch --list`, so patterns a project already has keep meaning what they already mean.

### Health

`GET /healthz` is liveness. `GET /readyz` is readiness, gated on a writable mirror root and on git actually being startable, because a deployment missing either looks healthy from outside and then fails every request.

## Configuration

```json
{
  "GitBranchStateCache": {
    "MirrorRoot": "/var/lib/gitbranchstatecache",
    "RefsTtl": "00:00:30",
    "AdmissionTtl": "00:01:00",
    "FetchTimeout": "00:02:00",
    "DiffTimeout": "00:02:00",
    "ProbeTimeout": "00:00:30",
    "MaxCachedDiffs": 2000,
    "MaxPathsPerRequest": 20000,
    "MirrorIdleMaxAge": "30.00:00:00",
    "Upstreams": {
      "github": {
        "BaseUrl": "https://github.com",
        "Repositories": ["studio/game.git"]
      },
      "ado": {
        "BaseUrl": "https://dev.azure.com/myorg",
        "Repositories": ["myproject/_git/game"]
      }
    }
  }
}
```

Startup validation refuses to run with no upstreams, an upstream with an empty or missing `Repositories` list, a pattern naming no literal segment, an unwritable mirror root, a `RefsTtl` above `AdmissionTtl`, or a non-positive timeout, and reports every problem at once.

## Deployment

Deploy **adjacent to the forge, not on-premises**. Its cost is round trips and its clients are worst served on residential links, which is the opposite placement from an object cache. `deploy/k8s` is a kustomize base: a StatefulSet with a read-write-once volume, a service, an ingress and a configmap. Start at one replica; each replica holds its own mirrors and diff cache, so replicas multiply fetch traffic and disk without improving the hit rate.

The volume is sized by the allow-list rather than by traffic, which is what makes it possible to provision in advance. Mirrors that go unqueried for longer than `MirrorIdleMaxAge` are deleted, because a deleted mirror costs one clone if it is asked for again, which is the cheapest possible way to be wrong.

## Instrumentation

A `ktsu.GitBranchStateCache` meter, with no exporter pinned. The pair worth watching is diff cache hits against misses, which reflects how well merge bases are clustering; a low ratio means the team is spread across many integration points and `MaxCachedDiffs` may need raising. The counter worth alerting on is `unknown_base`: every one of those is a client falling back to the local computation this service exists to replace.

## Security posture

This service holds read-only copies of source, which makes it a more attractive target than a blob cache.

- Admission is the only control over who is served, and there is no path through it that admits without a successful upstream call. It does not fail open when the forge is unreachable.
- The allow-list is checked **before** admission. Reversed, an unlisted repository would still be probed against the forge with the caller's credential before being refused, which would turn this service into an oracle for which repositories a credential can read.
- A caller's credential never reaches a git command line. It is handed over as configuration through the environment, because on Linux a command line is world readable through `/proc` and an environment block is not, and this process handles many different people's forge credentials.
- Host git configuration cannot influence a run: system and global configuration are switched off, inherited `GIT_*` variables are dropped, and credential helpers are disabled, so nothing configured for the account this runs as can answer on a caller's behalf.

## Design notes

The reasoning behind the choices above is in the source, next to the code each one constrains. The
ones worth knowing before changing anything:

- **No forge REST APIs, and therefore no per-forge adapter.** `ls-remote`, `fetch`, `merge-base` and
  `diff-tree` behave identically against GitHub and Azure DevOps and have no file-count cap. This is
  the decision the rest of the design hangs off.
- **`git diff-tree -r -z --no-renames`, not `git diff --raw`.** Plumbing, so the output does not
  change with configuration; NUL-delimited, so no path has to be unquoted; renames reported as a
  delete and an add, so the answer does not depend on a similarity heuristic.
- **The allow-list is checked before admission**, so an unlisted repository produces no upstream call
  at all.
- **A clone lands in a staging directory** and is moved into place only once it has finished, so a
  crash mid-clone cannot leave something that looks like a complete mirror.
- **A commit named by a client must be a full object id.** Refusing revision expressions means the
  arguments this service builds are only ever ids it has already recognised.

## Contributing

Contributions are welcome. Please open an issue or a pull request.

## License

MIT. See [LICENSE.md](LICENSE.md).
