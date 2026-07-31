# QA Strategy

Use this guide when writing or evaluating automated tests for Core and Client.

Run QA claim-first:

- Name the claim the change must prove.
- Choose the cheapest reliable evidence that proves that claim directly.
- Do not use source assertions to prove rendered UI, native behavior, or service behavior when better evidence is available.
- When evidence is local or manual, record the claim, environment, steps, result, and artifacts.

## Agent QA Gate

Before writing or accepting a test for this repo, record the QA decision in the issue brief, test plan, or implementation notes:

- **Claim:** observable behavior the test or check must prove.
- **Preferred evidence:** the surface and artifact from the matrix below.
- **Forbidden evidence:** evidence that would couple the check to implementation shape.
- **Source-test allowlist needed:** yes/no; if yes, cite or add the allowlist row below.

Use this gate before the first TDD tracer bullet and again whenever adding another test surface.

## Choose Evidence

Use this QA Evidence Matrix before adding or accepting tests.

| Claim area | Preferred evidence | Run | Use for |
| --- | --- | --- | --- |
| Core behavior | Integration tests through public Core APIs, game-session builders, and observable `ModeratorInstruction` / `ModeratorResponse` results. | CI | Rules, phase flow, serialization, status effects, and game invariants. |
| Moderator UI behavior | bUnit rendered component tests against the host-agnostic RCL public surface, then DOM, text, attribute, and event assertions. | CI | What the Moderator sees, can press, can expand, or can submit. |
| Client orchestration | Service and adapter tests with fakes for audio, haptics, persistence, wake lock, and app lifecycle seams. | CI | `GameClientManager`, display flow, save/recovery, audio reconciliation, mute state, roster projection, stats, and wake-lock policy. |
| Browser-rendered UI | Browser QA host fixtures, viewport presets, screenshots, DOM/CSS inspection, and agent-assisted visual review. | Local | Layout, safe-area-like spacing, scroll behavior, animation feel, focus order, and visual hierarchy. |
| Native platform behavior | Manual device checklist with captured observations. | Local/manual | Native audio, haptics, wake lock, resume/background behavior, platform storage, packaging, WebView quirks, and touch feel. |
| Simulator/cache contracts | Deterministic unit tests, Core integration tests, terminal-cache codec and local-record tests, and replay tests from fixed Run Seed Material. | CI | Canonical identities, simulation evidence, screening/probability interpretation, cache round trips, invalidation, and fallback orchestration. |
| Static policy/source contracts | Structured parsing or narrow source scans over manifest, plist, XAML, project, resource, CSS token, or documentation files. | CI when stable. | The source shape itself: permissions, metadata, no inline colors, token contrast, resource-backed copy, architecture boundaries, or QA policy sections. |

## Source-Test Rules

- Prefer behavior, rendered, service, adapter, or integration tests.
- Assert resource-backed copy through the generated resource/localization accessors that production uses; derive expected localized values from resource contract data instead of hardcoding localized copy.
- Treat service, component, and integration tests the same way: use production localization accessors or behavior assertions instead of freezing localized copy.
- Write a source-level test only when the protected claim is the source shape itself, or when the test is an allowlisted temporary scaffold.
- Reject raw Razor assertions over component names, event handler names, parameter declarations, CSS classes, resource key names, and implementation methods unless the test is allowlisted.
- Keep source, style, selector, and CSS class assertions to policy, allowlisted, or contractual claims; do not duplicate incidental HTML or CSS shape in rendered component tests.
- List every retained source-level test in the Source-Test Allowlist with its exact test or test group, category, protected claim, and replacement or removal condition.
- Put the same replacement or removal direction in a short code comment for every `Deprecated temporary scaffold`.
- Remove a temporary scaffold as soon as its replacement evidence exists.

## Allowed Source Tests

| Category | Rule |
| --- | --- |
| Permanent policy | Keep only when the source file is the real contract, such as platform metadata, resource-backed copy, design tokens, contrast thresholds, architecture boundaries, or this QA strategy. |
| Deprecated temporary scaffold | Keep only while rendered, browser-host, service, or adapter evidence is unavailable. The allowlist row must say what replaces it or when to remove it. |
| Disallowed | Do not keep source tests that freeze incidental implementation text, duplicate stronger evidence, or lack an allowlist row. |

## CI vs Local Evidence

Use CI for deterministic evidence:

- Core behavior through public Core APIs.
- Client services and adapters with fakes at external seams.
- Rendered component tests through the shared RCL and bUnit fixture.
- Simulator/cache contracts through deterministic units, Core integrations, terminal-cache codec and local-record checks, and narrow replay tests from fixed Run Seed Material.
- Narrow source-policy tests listed in the Source-Test Allowlist.
- Documentation contract tests that guard this guide.

## .NET 10 macOS Build Startup Recovery

Do not set `DOTNET_CLI_USE_MSBUILDNOINPROCNODE` to `1`, `true`, or `yes`, or set `MSBUILDNOINPROCNODE=1`, for routine verification on .NET 10 on macOS. The [.NET CLI environment-variable documentation](https://learn.microsoft.com/dotnet/core/tools/dotnet-environment-variables) confirms that the CLI variable sets `MSBUILDNOINPROCNODE=1` inside the `dotnet` entry process, so checking only the raw MSBuild variable in the caller is insufficient. This repo has confirmed that either setting can leave `dotnet build` and `dotnet test` sleeping during Build target startup with no compiler, MSBuild worker, or testhost child, no console output, and an absent or zero-byte binlog. CLI and MSBuild startup, project-property evaluation, and `dotnet test --no-build --list-tests` can still succeed in this state, so those checks do not clear the Build target path.

A separate sandbox boundary affects normal full-solution verification: `dotnet build Werewolves.sln --no-restore` can stall or fail when MAUI/iOS targets reach mobile task-host IPC inside the sandbox, while the exact unchanged command is confirmed to complete outside the sandbox in about 10 seconds with zero warnings and errors. This signature has mobile target or log events, CPU activity, or task-host progression; the `MSBUILDNOINPROCNODE=1` startup failure has no child, binlog event, or build progression.

Diagnose and recover one bounded layer at a time:

1. Run `printenv DOTNET_CLI_USE_MSBUILDNOINPROCNODE` and `printenv MSBUILDNOINPROCNODE`. If either prints an enabling value, remove both from the verification command and current shell with `unset DOTNET_CLI_USE_MSBUILDNOINPROCNODE MSBUILDNOINPROCNODE`; also remove both from agent command templates.
2. Confirm SDK and MSBuild startup with `dotnet --info` and `dotnet msbuild -version -nologo`.
3. Confirm project evaluation without running build targets with `dotnet msbuild Werewolves.Core/Werewolves.Core.Tests/Werewolves.Core.Tests.csproj -nologo -getProperty:TargetFramework`.
4. When an existing test assembly is available, isolate test discovery with `dotnet test Werewolves.Core/Werewolves.Core.Tests/Werewolves.Core.Tests.csproj --no-build --no-restore --list-tests`.
5. With both variables unset so MSBuild uses its normal in-process node, run the normal build and then `dotnet test --no-build`. Give each command a caller-enforced time limit, run only one build probe at a time, and stop only the process or process group started by that probe if it stalls.
6. If the normal full-solution build reaches MAUI/iOS task-host work and then stalls or fails inside the sandbox, retry the exact unchanged `dotnet build Werewolves.sln --no-restore` command with the required sandbox escalation. Do not add MSBuild diagnostic overrides to compensate for a sandbox IPC boundary.

`MSBUILDDISABLENODEREUSE=1`, serial execution through `-m:1` or `-p:BuildInParallel=false`, and `dotnet build-server shutdown` test different hypotheses; do not treat them as substitutes for, or reasons to set, either out-of-process-node variable. Shut down build servers only when no other build is active. Remove experimental MSBuild environment variables and diagnostic-only flags before recording normal verification evidence.

## Simulator And Cache QA

Simulator/cache tests must stay deterministic. Do not make CI pass/fail depend on live random distributions, probabilistic thresholds, wall-clock performance, timing, memory use, or implementation diagnostics.

Use these evidence surfaces:

- Deterministic unit tests for pure value contracts: Canonical Role Composition, Canonical Simulation Scenario, Role Lock-In and physical-card-zone values, branch-screening aggregation, Run Seed Material, identity/invalidation comparison, Completed versus Incomplete Simulation Run classification, Possible Game Result inventory, probability aggregation, and rounded display projections.
- Core integration tests through public Core APIs for layered lobby gates, already-decided classification, conditional setup derived from every reachable Role, Thief choice/exchange/recovery behavior, branchwise degenerate screening interpretation, probability batch interpretation, narrow replay coverage from Run Seed Material, and simulator-supported versus simulator-unsupported behavior.
- Terminal-cache codec and local-record tests for semantic round trips, exact current-capability identity, stale or foreign record rejection, and the rule that app-facing cache records do not carry per-run simulation evidence.
- Client service and adapter tests with fakes for cache lookup, local fallback persistence, fallback timeout, on-device evaluation failure, retry after failure, setup changes during evaluation, and simulator-unsupported unavailable state.

Canonical identity tests should prefer structural assertions and round-trip or parse checks over broad exact string assertions. Assert role counts by enum value, sort order by enum identifiers, zero-count omission, Actor Setup Card exclusion, `players=N`, and non-default assumptions as structured fields. For a Thief-enabled `P + 2` Role Composition, also assert a `P`-card Deal Pool with exactly one Thief, two distinct offer-instance identities with no offered Thief, and partition-sensitive Canonical Simulation Scenario identity. Distinct physical instances must remain distinct even when their behaviorally identical screening branches deduplicate. Use shared constants or value objects for separators, labels, profile ids, and version ids where they exist. Exact literal string assertions are allowed only at the narrow serializer/cache-key boundary when the literal format itself is intentionally the public or cache contract.

When a ticket distinguishes unknown, known-empty, and known-non-empty domain states, evidence must establish each state through the appropriate public surface; unknown must never stand in for empty. For validated multi-value input, prove through public behavior that incomplete or invalid submissions do not mutate state and that one accepted submission commits the complete value atomically.

Replay tests are not primary simulator correctness tests. Do not create a replay test by running an arbitrary stochastic scenario once, copying its outcome, and treating that as an oracle. Use replay tests only to prove determinism plumbing, known-oracle scenarios, or regression seeds from a diagnosed bug. For determinism plumbing, assert that the same Run Seed Material under the same simulator/profile version reproduces the same stable source record. For known-oracle scenarios, assert the independently derivable completion state, Game Session Outcome, ending Turn, and Victory Check Window. For regression seeds, assert the smallest fixed property that prevents the bug from returning.

Already-decided classification should be covered by deterministic classifier tests with rule-derived scenarios, including proof that an offer-only Role does not contribute initial Deal Pool coverage. Degenerate classification should mostly be covered with synthetic batch evidence: all 1,000 completed runs ending by Turn 1 means degenerate; one Turn 2 completed run means not degenerate; one Incomplete Simulation Run means could not evaluate. For Thief, independently supply every semantically distinct legal `Offer1`, `Offer2`, and `Decline` branch, then test aggregation: any Degenerate branch blocks; all completed non-degenerate branches pass; otherwise a failure, timeout, or incomplete branch produces nonblocking Could Not Evaluate. Do not try to maintain tests for every possible degenerate setup. A small number of known-oracle integration tests is enough to prove the simulator path, including one prevalidated degenerate setup.

Thief setup and runtime evidence must stay layered. Use value tests for the `P + 2`/Deal Pool/offer partition and physical instance identity; public-API Core integration tests to prove only the Deal Pool receives seeded assignments, exactly one Thief holder exists, conditional setup is required for Roles found only in offers, and committed choice/recovery cannot repeat an exchange or reopen `Decline`; and rendered client tests to prove the private surface shows only Core-provided `Offer1`, `Offer2`, and legal `Decline` options without asking the Moderator to re-enter the locked pair. Prove that an exchange makes the selected offer Player-owned and moves the original Thief card plus the unchosen offer to Set-Aside, while `Decline` retains Thief and moves both offers to Set-Aside. Recovery evidence must kill and rehydrate immediately after successful response processing and prove ADR-0017's atomic stable checkpoint restores the resulting zones, Role/fresh state, and pending sleep instruction without replaying the response.

Probability aggregation should be tested with handcrafted Simulation Result Evidence, not live 10,000-run batches. Cover Completed-only denominators, Incomplete Simulation Run exclusion, zero-frequency Possible Game Results, Shared Victory Outcomes and No-Winner Outcomes as Game Results, Game Result Frequency by Turn summing to Game Result Frequency, Ended-By-Turn Frequency derivation, and display rounding/grouping behavior.

Fallback runtime behavior belongs in deterministic service and adapter tests. In every evaluation depth, cover exact current-capability local cache hits, stale, foreign, or missing records starting fallback, failure, timeout, incomplete fallback collapsing to "could not evaluate", setup changes discarding stale in-flight work, simulator-unsupported state without a fallback attempt, Lobby Exit gating while pending versus after resolution, and the absence of an in-progress skip or dismiss action. For production `DegenerateScreeningOnly`, prove that already-decided and degenerate terminal classifications persist, a successful non-degenerate screening pass is nonblocking and not persisted, and failure plus simulator-unavailable states expose no retry or evaluation panel. For Thief branch orchestration, cover per-branch pending, stale, failure, and timeout outcomes; any completed Degenerate branch remains blocking, while terminal failure or timeout releases Lobby Exit when no branch is Degenerate and leaves the aggregate as Could Not Evaluate. For dormant `FullProbability`, retain coverage that successful terminal probability evaluation persists and retry is available only after failure.

Do not add simulator/cache source-level tests for this work. Prove simulator/cache claims through behavior, parser or serializer round trips, local-record checks, Core integration tests, and service/adapter tests.

Golden fixtures are small checked-in input/expected-output artifacts that protect stable contracts. They are appropriate for canonical strings, minimal locked Deal Pool/offer partitions, branch-screening aggregates, minimal simulation evidence records, Possible Game Result inventories, and terminal cache records. They are not snapshots of full transcripts, raw engine traces, exception details, timing, memory, UI screenshots, or random win-rate expectations. Updating a golden fixture is a contract change and should be reviewed with the claim it protects.

Keep simulator/cache golden fixtures small: canonical identity examples, minimal Simulation Result Evidence examples, probability aggregation input/output, and terminal cache records for already-decided, degenerate, and probability entries. Do not add full-run replay fixtures except for known-oracle scenarios or regression seeds.

Do not normally check runtime diagnostic reports into the repo. Keep small golden fixtures for stable contracts and attach one-off diagnostic output to issue evidence when it helps explain a failure.

Keep this guide at the stable QA policy and simulator/cache evidence-contract level. Concrete test class names, fixture file paths, diagnostic schema field names, and implementation sequencing belong in issues unless a contract must be shared across issues.

Before claiming a simulator/cache implementation issue done, record the Agent QA Gate fields for each test surface used. The issue needs deterministic CI evidence for the behavior being claimed.

## bUnit RCL Fixture

Use `ModeratorComponentTestContext` for shared Moderator UI component tests.

- Reference `Werewolves.Client.Shared` directly from the test project.
- Set `pt-PT` culture and `ClientStrings.Culture` before rendering.
- Register fake or no-op services for audio, haptics, wake lock, persistence, and any other host-only dependency.
- Prove user-visible behavior through rendered markup and event interaction rather than raw Razor source assertions.
- Use `ClientStrings`, `GameStrings`, or production-derived localization helpers for resource-backed labels and text assertions; do not hardcode localized UI copy in component tests.
- Treat legacy `Renderer`/`BL0006` component tests as existing interaction scaffolds only; new shared component coverage should use bUnit unless it is deliberately replacing one of those scaffolds.
- `SelectPlayersViewBunitTests` is the replacement evidence for the removed `SelectPlayersViewMarkupTests` source allowlist row.
- `InstructionRendererBunitTests.SelectOptionsInstruction_RendersCoreProvidedOptionControlsAndSingleInputActionZone` is the replacement evidence for the removed `InstructionRendererTests.Markup_HasSelectOptionsInstructionBranch` source allowlist row.

Keep local/manual evidence out of CI until it has stable fixtures, runtime, and pass/fail contracts:

- Browser QA host inspection and screenshot review.
- Exploratory device checks.
- Native audio, haptics, wake lock, resume/background, platform storage, packaging, WebView quirks, and touch feel.
- Broad visual review that still requires human judgment.

## Manual Device Checks

Treat these as the manual device boundary. The repeatable client checklist lives in `Werewolves.Client/docs/native-device-qa-checklist.md`.

The checklist stays native-only:

- It proves native audio, haptics, wake lock, resume/background, platform storage, packaging/install, WebView host quirks, and touch feel with captured observations from real devices.
- It does not replay Core game rules, phase flow, serialization semantics, or which semantic sound effects Core should trigger.
- It does not repeat Browser QA Host layout, CSS, viewport, focus, or deterministic rendered-state checks.
- It remains manual unless future repeated platform regressions justify one or two tiny device smoke tests with stable pass/fail evidence.

Manual check notes must include: claim, device/OS, app build, steps, result, and screenshots, video, logs, install results, or observations when useful.

## Browser QA Host

Use the Browser QA Host for local rendered Moderator UI inspection. It lives in `Werewolves.Client.BrowserQaHost` and consumes the same shared RCL boundary used by the MAUI host and bUnit tests.

- Run it locally with `dotnet run --project Werewolves.Client.BrowserQaHost/Werewolves.Client.BrowserQaHost.csproj`, then open `http://localhost:5098`.
- Use `/?qa=lobby`, `/?qa=probability`, `/?qa=degenerate`, `/?qa=dashboard`, and `/?qa=victory` for deterministic access to ordinary role selection, a screening-passed negative check, the retained degenerate warning, dashboard/action instruction, and victory states.
- Run the narrow browser automation smoke/layout check on demand with `dotnet test Werewolves.Client.BrowserQa.Tests/Werewolves.Client.BrowserQa.Tests.csproj -- Playwright.BrowserName=chromium`.
- Install the Playwright Chromium browser before the first local run after package restore/build. With PowerShell available, run `pwsh Werewolves.Client.BrowserQa.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`; otherwise run the Playwright package CLI for the restored `Microsoft.Playwright` version, for example `node ~/.nuget/packages/microsoft.playwright/1.59.0/.playwright/package/cli.js install chromium`.
- Inspect the shared `Routes` root from `Werewolves.Client.Shared`; the browser host must not copy or fork lobby, dashboard, instruction, or victory pages.
- Treat haptics, wake lock, audio playback, and local save storage as browser-safe substitutes: no-op, disabled, or in-memory local QA behavior only.
- Inspect real HTML/CSS at mobile and desktop-sized viewports.
- Use screenshots, DOM inspection, computed styles, and agent-assisted visual feedback.
- Use it for layout, spacing, typography, motion, focus, and accessibility attributes.
- Do not treat the browser host as a public deployment target, account or multiplayer surface, production persistence surface, or second maintained web product.
- Do not add a required CI gate for browser-host launch or smoke checks. Any browser smoke check should stay local or run-on-demand, lightweight, semantic, and focused on launch plus representative Moderator UI access rather than visual regression coverage.
- Keep browser automation as a small smoke/layout evidence layer for browser-observable facts: host launch, deterministic scenario access, loaded CSS, viewport geometry, computed styles, focus order, and scroll behavior. It does not replace Core integration tests, client service tests, bUnit rendered component tests, native device checks, or broad visual-regression baselines.
- Pair browser findings with deterministic CI tests whenever a claim can be narrowed.

## Source-Test Allowlist

Rows are active policy. If a retained source-level test no longer matches its condition, replace it or remove it.

| Test or group | Category | Protected claim | Condition |
| --- | --- | --- | --- |
| `Werewolves.Client.Tests.Documentation.QaStrategyTests.QaStrategy_DefinesClaimFirstEvidenceGuideAndSourceTestAllowlist` | Permanent policy | This guide keeps claim-first QA, evidence selection, source-test rules, CI/local split, manual device boundary, browser-host guidance, and a populated allowlist. | Markdown is the policy surface; keep a narrow documentation contract test. |
| `Werewolves.Client.Tests.Documentation.QaStrategyTests.QaStrategy_SourceTestAllowlistTracksActiveRetainedSourceTests` | Permanent policy | The source-test allowlist tracks active retained source tests and omits retired deleted scaffolds. | Markdown is the policy surface; keep a narrow documentation contract test. |
| `Werewolves.Client.Tests.Documentation.QaStrategyTests.NativeDeviceChecklist_DefinesManualOnlyClaimAndEvidenceChecks` | Permanent policy | The client docs keep a manual-only native device checklist with named claims, manual scenarios, expected evidence, native behavior coverage, and explicit Core/browser-host boundaries. | Markdown is the policy surface; keep a narrow documentation contract test. |
| `Werewolves.Client.Tests.Resources.LocalizationPolicyTests.TestProjects_DoNotHardcodeLocalizedProductionCopy` | Permanent policy | Client and Core tests do not hardcode localized production copy. | The client/Core test trees and generated resource files are the contract. |
| `Werewolves.Client.Tests.Platform.AndroidManifestTests.AndroidManifest_DeclaresVibratePermissionForHapticFeedback` | Permanent policy | Android declares the haptics permission needed by MAUI haptic feedback. | Manifest XML is the contract. |
| `Werewolves.Client.Tests.Resources.ClientStringsTests.ClientStrings_ExposesPortugueseUiCopyThroughGeneratedAccessor` | Permanent policy | Portuguese client UI copy remains resource-backed through the generated accessor. | The generated accessor is the runtime localization surface. |
| `Werewolves.Client.Tests.BrowserQaHost.BrowserQaHostCompositionTests.BrowserQaHostProject_ReferencesSharedBoundaryWithoutMaui` | Permanent policy | The browser QA host project stays on the host-agnostic shared boundary without MAUI. | Project XML is the architecture-boundary contract. |
| `Werewolves.Client.Tests.Styling.DarkThemeTokenTests`: `AppCss_ConsumesColorValuesThroughDesignTokens`, `RootDocument_UsesDarkThemeTokensBeforePagesRender`, `MauiHost_UsesDarkChromeAcrossSupportedSurfaces`, `TextTokens_HaveReadableContrastAgainstDarkSurfaces`, `Pages_DoNotUseInlineColorLiterals` | Permanent policy | Dark theme tokens, first-paint defaults, platform chrome metadata, contrast, and no-inline-color policy stay intact. | CSS, project, and platform metadata are the contracts. |
| `Werewolves.Client.Tests.Styling.InstructionTransitionTests.DesignTokens_DefineInstructionAnimationDurationBetween200And300Ms` | Permanent policy | Instruction animation duration stays in the approved 200-300 ms token range. | CSS token value is the contract. |
