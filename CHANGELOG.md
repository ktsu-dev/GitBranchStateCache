## v1.1.1 (patch)

Changes since v1.1.0:

- Stop git discovery at the mirror directory [patch] (Claude)
- Remember when a branch shares no history with the base [patch] (Claude)

## v1.1.1-pre.2 (prerelease)

Changes since v1.1.1-pre.1:

- Bump Testably.Abstractions.Testing from 7.1.1 to 7.1.2 (dependabot[bot])

## v1.1.1-pre.1 (prerelease)

Changes since v1.1.0:

- Bump MSTest.Sdk from 4.5.1 to 4.5.2 (dependabot[bot])
- Bump the system group with 1 update (dependabot[bot])
- Bump the microsoft group with 1 update (dependabot[bot])

## v1.1.0 (minor)

Changes since v1.0.0:

- Check the cached merge base node for null explicitly [patch] (matt-edmondson)
- Cache merge bases, work on branches in parallel, and fetch in the background [minor] (matt-edmondson)
- Match POSIX character classes in branch patterns [patch] (Claude)
- Name no paths when a state request sends an empty paths list [patch] (Claude)
- Key a repository requested with and without a trailing .git as one mirror [patch] (matt-edmondson)
- Raise ktsu.Essentials to 2.10.7 and Testably interface to 10.4.0 (Claude)
- Merge main into dependabot/nuget/Testably.Abstractions.Testing-7.1.1 (Claude)
- Resolve a relative --mirror-root instead of aborting startup [patch] (Claude)
- Stop the admission probe decoding output it never reads, and guard the large non-UTF-8 diff [patch] (Claude)
- Keep a coalesced clone or fetch running when the request that started it disconnects [patch] (Claude)
- Match 401/403 only in git's own phrasing when classifying a refused probe [patch] (Claude)
- fix: sweep staging directories left by clones that never finished [patch] (Claude)
- fix: make --allow replace an upstream's configured allow-list [patch] (Claude)
- Move CI onto the shared ci-shared.yml pipeline (Claude)
- Bump ktsu.RunCommand to 1.9.1 for the cancellation kill fix [patch] (Claude)
- Merge remote-tracking branch 'origin/main' into claude/delegate-git-to-runcommand (matt-edmondson)
- Delegate git process invocation to ktsu.RunCommand [patch] (Claude)
- Read a bracket member's range in its own helper (Claude)
- fix: stop the idle-mirror sweep reaping a live mirror under a mirror.git path segment [patch] (Claude)
- fix: honour [...] bracket expressions and backslash escapes in branch patterns [patch] (Claude)
- fix: canonicalize the upstream key, not just the repository path [patch] (Claude)
- Gate Dependabot auto-merge on CI actually being green (Claude)
- fix: keep the sweep from reaping a mirror that is in use [patch] (Claude)
- style: drop a redundant interpolation prefix in the new seed [patch] (matt-edmondson)
- fix: canonicalise the repository path so one repository is one mirror [patch] (matt-edmondson)
- ci: adopt the consolidated .NET workflow [patch] (Claude)
- test: let Uri build the file URI for the upstream path [patch] (matt-edmondson)
- ci: make the SonarQube quality gate opt in [patch] (matt-edmondson)
- ci: adopt the unified dotnet workflow [patch] (matt-edmondson)

## v1.0.42 (patch)

Changes since v1.0.41:

- Match POSIX character classes in branch patterns [patch] (Claude)

## v1.0.42-pre.1 (prerelease)

Changes since v1.0.41:

- Bump the microsoft group with 1 update (dependabot[bot])

## v1.0.41 (patch)

Changes since v1.0.40:

- Name no paths when a state request sends an empty paths list [patch] (Claude)
- Key a repository requested with and without a trailing .git as one mirror [patch] (matt-edmondson)

## v1.0.40 (patch)

Changes since v1.0.39:

- Raise ktsu.Essentials to 2.10.7 and Testably interface to 10.4.0 (Claude)
- Merge main into dependabot/nuget/Testably.Abstractions.Testing-7.1.1 (Claude)

## v1.0.39 (patch)

Changes since v1.0.38:

- Resolve a relative --mirror-root instead of aborting startup [patch] (Claude)

## v1.0.38 (patch)

Changes since v1.0.37:

- Stop the admission probe decoding output it never reads, and guard the large non-UTF-8 diff [patch] (Claude)

## v1.0.38-pre.1 (prerelease)

Changes since v1.0.37:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.37 (patch)

Changes since v1.0.36:

- Keep a coalesced clone or fetch running when the request that started it disconnects [patch] (Claude)

## v1.0.37-pre.2 (prerelease)

Changes since v1.0.37-pre.1:

- Bump MSTest.Sdk from 4.4.1 to 4.5.1 (dependabot[bot])
- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.37-pre.1 (prerelease)

Changes since v1.0.36:

- Bump the ktsu group with 3 updates (dependabot[bot])

## v1.0.36 (patch)

Changes since v1.0.35:

- Match 401/403 only in git's own phrasing when classifying a refused probe [patch] (Claude)

## v1.0.36-pre.3 (prerelease)

Changes since v1.0.36-pre.2:

- Bump Polyfill from 11.4.1 to 11.4.3 (dependabot[bot])

## v1.0.36-pre.2 (prerelease)

Changes since v1.0.36-pre.1:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.36-pre.1 (prerelease)

Changes since v1.0.35:

- Bump the ktsu group with 3 updates (dependabot[bot])

## v1.0.35 (patch)

Changes since v1.0.34:

- fix: sweep staging directories left by clones that never finished [patch] (Claude)
- fix: make --allow replace an upstream's configured allow-list [patch] (Claude)

## v1.0.34 (patch)

Changes since v1.0.33:

- Move CI onto the shared ci-shared.yml pipeline (Claude)

## v1.0.33 (patch)

Changes since v1.0.32:

- Bump ktsu.RunCommand to 1.9.1 for the cancellation kill fix [patch] (Claude)
- Merge remote-tracking branch 'origin/main' into claude/delegate-git-to-runcommand (matt-edmondson)
- Delegate git process invocation to ktsu.RunCommand [patch] (Claude)

## v1.0.33-pre.1 (prerelease)

Changes since v1.0.32:

- Bump Polyfill from 11.4.0 to 11.4.1 (dependabot[bot])
- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.32 (patch)

Changes since v1.0.31:

- Read a bracket member's range in its own helper (Claude)
- fix: stop the idle-mirror sweep reaping a live mirror under a mirror.git path segment [patch] (Claude)
- fix: honour [...] bracket expressions and backslash escapes in branch patterns [patch] (Claude)

## v1.0.31 (patch)

Changes since v1.0.30:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.30 (patch)

Changes since v1.0.29:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.29 (patch)

Changes since v1.0.28:

- fix: canonicalize the upstream key, not just the repository path [patch] (Claude)

## v1.0.28 (patch)

Changes since v1.0.27:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.27 (patch)

Changes since v1.0.26:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.26 (patch)

Changes since v1.0.25:

- Bump Polyfill from 11.3.0 to 11.4.0 (dependabot[bot])
- Bump the ktsu group with 6 updates (dependabot[bot])

## v1.0.25 (patch)

Changes since v1.0.24:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.24 (patch)

Changes since v1.0.23:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.23 (patch)

Changes since v1.0.22:

- Bump MSTest.Sdk from 4.4.0 to 4.4.1 (dependabot[bot])
- Bump the ktsu group with 6 updates (dependabot[bot])

## v1.0.22 (patch)

Changes since v1.0.21:

- Gate Dependabot auto-merge on CI actually being green (Claude)

## v1.0.21 (patch)

Changes since v1.0.20:

- fix: keep the sweep from reaping a mirror that is in use [patch] (Claude)

## v1.0.20 (patch)

Changes since v1.0.19:

- style: drop a redundant interpolation prefix in the new seed [patch] (matt-edmondson)
- fix: canonicalise the repository path so one repository is one mirror [patch] (matt-edmondson)

## v1.0.19 (patch)

Changes since v1.0.18:

- Bump the ktsu group with 6 updates (dependabot[bot])

## v1.0.18 (patch)

Changes since v1.0.17:

- ci: adopt the consolidated .NET workflow [patch] (Claude)

## v1.0.17 (patch)

Changes since v1.0.16:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.16 (patch)

Changes since v1.0.15:

- Bump Polyfill from 11.2.0 to 11.3.0 (dependabot[bot])
- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.15 (patch)

Changes since v1.0.14:

- Bump the system group with 1 update (dependabot[bot])
- Bump the microsoft group with 2 updates (dependabot[bot])

## v1.0.14 (patch)

Changes since v1.0.13:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.13 (patch)

Changes since v1.0.12:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.12 (patch)

Changes since v1.0.11:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.11 (patch)

Changes since v1.0.10:

- Bump MSTest.Sdk from 4.3.3 to 4.4.0 (dependabot[bot])
- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.10 (patch)

Changes since v1.0.9:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.9 (patch)

Changes since v1.0.8:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.8 (patch)

No significant changes detected since v1.0.7.

## v1.0.7 (patch)

Changes since v1.0.6:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.6 (patch)

Changes since v1.0.5:

- test: let Uri build the file URI for the upstream path [patch] (matt-edmondson)
- ci: make the SonarQube quality gate opt in [patch] (matt-edmondson)
- ci: adopt the unified dotnet workflow [patch] (matt-edmondson)

## v1.0.5 (patch)

Changes since v1.0.4:

- Bump the ktsu group with 6 updates (dependabot[bot])

## v1.0.4 (patch)

No significant changes detected since v1.0.3.

## v1.0.3 (patch)

Changes since v1.0.2:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.2 (patch)

Changes since v1.0.1:

- Bump the ktsu group with 4 updates (dependabot[bot])

## v1.0.1 (patch)

Changes since v1.0.0:

- Bump the ktsu group with 2 updates (dependabot[bot])

## v1.0.0 (major)

- [patch] Run the git config tests outside any repository (matt-edmondson)
- [minor] Scaffold ktsu.GitBranchStateCache (matt-edmondson)

