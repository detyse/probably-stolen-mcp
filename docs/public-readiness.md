# Publication assessment

[简体中文](public-readiness.zh-CN.md) · [User guide](usage-guide.md)

Reviewed on **2026-11-07** against source baseline `e5c0c40` plus the bundled-help and documentation changes in the working tree.

**Recommendation: suitable for publication as an experimental source project.** The evidence does not yet support a stable, fully covered gameplay release. Repository visibility is a separate owner decision.

## Checks and fixes

| Area | Finding / change |
| --- | --- |
| Credentials and personal paths | Pattern scans of the working files and six existing reachable commits found no credential-like tokens, private keys, or Windows home-directory paths. This is a scoped scan, not a full security audit |
| Dependencies | NuGet audit of direct and transitive dependencies in four solution projects reported no known vulnerabilities on the review date |
| Distribution contents | No tracked game DLLs, executables, extracted asset packages, saves, or connection files were found. Compilation references game libraries with copying disabled; Mod packaging allows only its manifest and two project DLLs |
| Accidental file inclusion | Added ignore rules for connection files, environment files, private-key containers, binaries, and game data/save directories |
| Getting started | Added matching Chinese/English guides and an English README covering build, install, stdio setup, first action, daily workflow, and failures |
| Claims and provenance | Clarified the pre-rename gameplay evidence, current 151-check protocol suite, and unverified workflows; removed an invalid GitHub link to a generated artifact |
| Data flow and ownership | Explained that an AI client may forward tool results to its model provider; separated the project's MIT license from game content shown in evidence |
| Repeatable checks | Added Windows CI for the game-independent protocol/HTTP/stdio/help suite, with read-only repository permissions and pinned action revisions |
| Bundled help | Included the pending help implementation and operating topics so the guide's `get_help` instructions match the source |

The bridge binds to loopback, requires a per-start token, rejects browser Origin headers and unexpected Host values, and only exposes registered game actions. The MCP client restricts the bridge URL to loopback and disables proxies and redirects. These controls reduce exposure; they are not a claim of a comprehensive security review.

The official [modding code of conduct](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.md) encourages source publication, requires truthful capability/data disclosures, and prohibits redistribution of game source and extracted game assets. Gameplay screenshots and visible text in the evidence retain their original ownership.

## Before calling it a stable release

- Install the renamed `PSMcp.*` Mod from a fresh package and repeat the full open → trade → close → sleep workflow.
- Validate new-game selection, appraisal, machines, additional night locations, and other supported game versions before claiming their coverage.
- Test another machine and client configuration. The guides are verified against the scripts and current artifact layout, not a separate clean-PC installation.
- If shipping downloadable binaries, prepare versioned packages, dependency license notices, and checksums. Current onboarding builds from source.

These are release-quality follow-ups, not hidden claims that the experimental project already satisfies them. Publishing documentation does not change repository visibility or establish game compatibility.

## Verification records

[Migration checks](validation/2026-11-07-migration.json) distinguish compilation/protocol checks from historical gameplay. [Bundled-help checks](validation/2026-11-07-help.json) record help availability without a game connection. [Capabilities](capabilities.md) links the gameplay observations and their limitations.

The [publication-preparation verification record](validation/2026-11-07-public-readiness.json) records the successful build, 151 checks, 21 published-help checks, document example validation, dependency audit, and scoped repository scan.
