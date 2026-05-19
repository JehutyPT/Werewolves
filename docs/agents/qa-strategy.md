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
- Narrow source-policy tests listed in the Source-Test Allowlist.
- Documentation contract tests that guard this guide.

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
- Use `/?qa=lobby`, `/?qa=dashboard`, and `/?qa=victory` for deterministic access to lobby/role selection, dashboard/action instruction, and victory states.
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
| `Werewolves.Client.Tests.Styling.InstructionTransitionTests`: `AppCss_DefinesInstructionEnterKeyframes`, `AppCss_InstructionBlockUsesAnimationToken` | Deprecated temporary scaffold | Instruction enter animation exists and consumes the duration token. | Replace with browser-host computed-style or visual-motion checks. |
| `Werewolves.Client.Tests.Styling.DashboardOverlayLayoutTests`: `ProductionDashboard_FixesTopAndBottomOverlays`, `ProductionDashboard_AddsScrollPaddingForFixedOverlays`, `ProductionDashboard_StatusBarUsesInsetWidthInsteadOfViewportWidth` | Deprecated temporary scaffold | Production dashboard overlays and inset-aware scroll padding stay present. | Replace with browser-host viewport and computed-layout checks. |
