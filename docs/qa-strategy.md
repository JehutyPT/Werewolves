# QA Strategy

This repository uses a claim-first QA strategy: every change starts by naming the claim it must prove, then choosing the cheapest reliable evidence for that claim. Cheap evidence is useful only when it is still honest evidence. A narrow source assertion is acceptable for a source policy; it is weak evidence for rendered UI behavior.

The strategy applies to Core and Client work. PRD #53 client testing builds on this document; PRD #29 simulator transcript, replay, and invariant work is separate simulator QA.

## QA Evidence Matrix

| Claim area | Preferred evidence | CI or local | Use when | Notes |
| --- | --- | --- | --- | --- |
| Core behavior | Integration tests through public Core APIs, game-session builders, and observable `ModeratorInstruction` / `ModeratorResponse` results | CI | Rules, phase flow, serialization, status effects, and game invariants change | These tests stay independent of the MAUI client. Headless transcript and replay coverage belongs to simulator QA under PRD #29. |
| Moderator UI behavior | Rendered component tests through the component public surface, then DOM/text/attribute/event assertions | CI once the RCL boundary from ADR-0006 exists and the tests are stable | A claim is about what the Moderator sees, can press, can expand, or can submit | Raw Razor assertions are temporary scaffolding only when they protect a needed claim before rendered evidence is available. |
| Client orchestration | Service and adapter tests using fakes for audio, haptics, persistence, wake lock, and app lifecycle seams | CI | `GameClientManager`, display flow, save/recovery, audio reconciliation, mute state, roster projection, stats, or wake-lock policy changes | Prefer testing app-owned services over inspecting components that happen to call them. |
| Local browser inspection | Local browser QA host state fixtures, viewport presets, screenshot review, DOM/CSS inspection, and agent-assisted visual feedback | Local only for now | The claim involves actual rendered layout, safe-area-like spacing, scroll behavior, animation feel, focus order, or visual hierarchy | The browser host is a debugging and inspection tool, not a replacement for deterministic CI tests. |
| Native platform behavior | Manual device checks with a written checklist and captured observations | Local/manual | Native audio, haptics, wake lock, resume/background behavior, platform storage, packaging, WebView quirks, or touch feel changes | Device automation is intentionally deferred unless a later issue identifies tiny smoke tests with stable signal. |
| Static policy/source contracts | Structured parsing or narrow source scans over manifest, plist, XAML, project, resource, CSS token, or documentation files | CI when stable | The claim is the source shape itself: permission exists, dark chrome metadata exists, no inline color literals exist, tokens meet contrast, or QA policy sections exist | These tests must be listed in the Source-Test Allowlist and must not freeze incidental implementation text. |

## Manual Device Boundary

The following client claims remain manual unless a later issue justifies tiny, stable device smoke tests:

- Native audio routing, looping, interruption, and mute behavior on real Android, iOS, and Mac Catalyst devices.
- Haptic strength, timing feel, and platform availability.
- Wake lock engagement and release across foreground/background transitions.
- Resume/background behavior after operating-system process pressure.
- Platform storage behavior, including file permissions and app data cleanup.
- Packaging, signing, app icon, splash, and store metadata behavior.
- WebView quirks that only appear in platform hosts, including safe areas, viewport units, keyboard overlays, and platform CSS bugs.
- Touch feel: hit target comfort, accidental tap prevention, press-and-hold feel, scroll momentum, and dark-room readability.

Manual evidence should still be claim-first. The checklist should say what changed, what claim was checked, device/OS, steps, result, and any screenshots or notes.

## Local Browser QA Host

The local browser QA host is the planned human and agent-assisted inspection surface for rendered Moderator UI. It should run locally, seed deterministic app states, and expose the same Moderator UI states the MAUI WebView renders: lobby setup, role selection, dashboard tabs, instruction flows, selection inputs, assignment inputs, victory, empty/error states, and long content.

The host supports:

- Human inspection of real rendered HTML/CSS at mobile and desktop-sized viewports.
- Agent-assisted screenshot review, DOM inspection, and computed-style checks.
- Fast reproduction of UI states without driving a full physical game.
- Visual feedback loops for layout, spacing, typography, motion, and accessibility attributes.

The host does not make browser checks CI-blocking in this slice. Browser-host checks are local-only until the team has stable state fixtures, acceptable runtime, and clear pass/fail contracts. It also does not implement simulator transcript, replay, or invariant work.

## CI and Local Split

CI-worthy now or once the named boundary exists:

- Core behavior tests through public Core APIs.
- Deterministic client service and adapter tests.
- Rendered component tests after ADR-0006 extracts host-agnostic Razor components into an RCL.
- Narrow source-policy tests listed in the Source-Test Allowlist.
- Documentation contract tests that guard this QA strategy.

Local-only for now:

- Browser QA host inspection and screenshot review.
- Exploratory device checks.
- Manual checks for native audio, haptics, wake lock, resume/background, platform storage, packaging, WebView quirks, and touch feel.
- Broad UI visual review that still requires human judgment.

## Source-Test Policy

Source-level tests may stay only when one of these is true:

- `Permanent policy`: the source shape is the real contract, such as manifest permissions, dark platform chrome metadata, design tokens, contrast thresholds, resource-backed Portuguese copy, architecture boundaries, or this QA strategy.
- `Deprecated temporary scaffold`: the test protects a real claim while better evidence is intentionally deferred. The allowlist row must state the replacement layer or removal condition, and the test method must include a short comment naming the same direction.

Raw Razor assertions around component names, event handler names, parameter declarations, CSS classes, resource key names, and implementation methods are not durable evidence by default. Retain them only as deprecated scaffolds toward ADR-0006/bUnit rendered component tests, service/adapter tests, or local browser QA host checks.

## Existing Source-Test Audit

| Classification | Current decision |
| --- | --- |
| Permanent policy | Keep manifest, Portuguese UI copy, dark-theme chrome, color-token, contrast, no-inline-color, animation-duration-token, and QA-document contract tests where the source shape is the protected contract. |
| Deprecated temporary scaffold | Keep narrow raw Razor/CSS assertions only where they bridge to ADR-0006/bUnit rendered component coverage or the local browser QA host. Each retained method has a comment naming that replacement direction. |
| Convert to rendered/component test | Select/assign input view markup, instruction branch wiring, hold-button markup, dashboard overlay layout, shell wrapper, and instruction CSS keyframe usage. |
| Convert to service/adapter test | Haptic timing and game/client orchestration claims already have preferred evidence in `HoldButtonHapticSequenceTests`, `HoldButtonInteractionTests`, `InstructionDisplayFlowTests`, `GameClientManagerTests`, and related service tests. |
| Delete/merge | Raw assertions for implementation details already covered by behavior/rendered tests: hold-button delay/cancellation source text, haptic-service injection source text, haptic preset source text, confirmation-view hold-button source text, and dashboard page stale negative markup checks. |
| Already behavior/rendered evidence | Preserve existing rendered or service-style tests including dashboard interaction, dashboard audio toggle rendering, instruction collapsible behavior, assign-role interaction, hold-button interaction, hold-confirmation gate behavior, audio playback, wake lock, display flow, roster, stats, and game-client orchestration. |

## Source-Test Allowlist

| Test | Category | Protected claim | Rationale or replacement/removal condition |
| --- | --- | --- | --- |
| `Werewolves.Client.Tests.Documentation.QaStrategyTests.QaStrategy_DefinesClaimFirstEvidenceContractAndSourceTestAudit` | Permanent policy | The repository keeps a claim-first QA strategy, evidence matrix, manual boundary, browser-host guidance, CI/local split, and populated source-test allowlist. | The document is the policy surface; a narrow markdown contract test prevents silent removal of the QA contract. |
| `Werewolves.Client.Tests.Platform.AndroidManifestTests.AndroidManifest_DeclaresVibratePermissionForHapticFeedback` | Permanent policy | Android declares the haptics permission needed by MAUI haptic feedback. | The manifest permission is platform metadata; XML source inspection is the exact contract. |
| `Werewolves.Client.Tests.Resources.ClientStringsTests.ClientStrings_ExposesPortugueseUiCopyThroughGeneratedAccessor` | Permanent policy | Portuguese client UI copy remains resource-backed and available through the generated accessor. | The generated resource accessor is the runtime surface for localized UI copy; this is stronger than scanning `.resx` text. |
| `Werewolves.Client.Tests.Styling.DarkThemeTokenTests.AppCss_ConsumesColorValuesThroughDesignTokens` | Permanent policy | App CSS consumes colors through `--ww-*` tokens instead of ad hoc literals. | A source scan is the policy contract for CSS token discipline. |
| `Werewolves.Client.Tests.Styling.DarkThemeTokenTests.RootDocument_UsesDarkThemeTokensBeforePagesRender` | Permanent policy | The root document starts with dark theme background, text, and color scheme before pages render. | CSS source is the contract for first-paint theme defaults. |
| `Werewolves.Client.Tests.Styling.DarkThemeTokenTests.MauiHost_UsesDarkChromeAcrossSupportedSurfaces` | Permanent policy | MAUI, Android, iOS, Mac Catalyst, icon, and splash metadata stay aligned to dark chrome. | Platform metadata and project-file source are the durable contract for pre-WebView chrome. |
| `Werewolves.Client.Tests.Styling.DarkThemeTokenTests.TextTokens_HaveReadableContrastAgainstDarkSurfaces` | Permanent policy | Text and accent tokens meet the contrast threshold against dark surfaces. | Token values are the source policy; computed contrast is deterministic and CI-worthy. |
| `Werewolves.Client.Tests.Styling.DarkThemeTokenTests.Pages_DoNotUseInlineColorLiterals` | Permanent policy | Razor pages avoid inline color literals and inherit theme tokens. | A source scan is the cheapest reliable evidence for the static no-inline-color policy. |
| `Werewolves.Client.Tests.Styling.DarkThemeTokenTests.Pages_RenderInsideDarkShells` | Deprecated temporary scaffold | Pages keep the expected dark shell wrapper. | Replace with ADR-0006/bUnit or browser-host rendered checks that verify shell semantics without freezing Razor text. |
| `Werewolves.Client.Tests.Styling.InstructionTransitionTests.DesignTokens_DefineInstructionAnimationDurationBetween200And300Ms` | Permanent policy | Instruction animation duration stays in the approved 200-300 ms token range. | The design token value is the policy surface. |
| `Werewolves.Client.Tests.Styling.InstructionTransitionTests.AppCss_DefinesInstructionEnterKeyframes` | Deprecated temporary scaffold | Instruction enter animation exists in app CSS. | Replace with browser-host computed-style or visual-motion checks. |
| `Werewolves.Client.Tests.Styling.InstructionTransitionTests.AppCss_InstructionBlockUsesAnimationToken` | Deprecated temporary scaffold | Instruction blocks consume the animation token. | Replace with browser-host computed-style checks for rendered instruction blocks. |
| `Werewolves.Client.Tests.Styling.DashboardOverlayLayoutTests.ProductionDashboard_FixesTopAndBottomOverlays` | Deprecated temporary scaffold | Production dashboard top and bottom overlays stay fixed. | Replace with local browser QA host viewport/computed-layout checks. |
| `Werewolves.Client.Tests.Styling.DashboardOverlayLayoutTests.ProductionDashboard_AddsScrollPaddingForFixedOverlays` | Deprecated temporary scaffold | Dashboard content reserves scroll padding for fixed overlays. | Replace with local browser QA host viewport/computed-layout checks. |
| `Werewolves.Client.Tests.Styling.DashboardOverlayLayoutTests.ProductionDashboard_StatusBarUsesInsetWidthInsteadOfViewportWidth` | Deprecated temporary scaffold | Dashboard status bar respects inset width rather than full viewport width. | Replace with local browser QA host safe-area and viewport checks. |
| `Werewolves.Client.Tests.Components.HoldButtonMarkupTests.Markup_UsesPointerEventsForHoldDetection` | Deprecated temporary scaffold | Hold button uses pointer events suitable for touch and pointer cancellation. | Replace with ADR-0006/bUnit rendered event tests for pointer down/up/leave/cancel. |
| `Werewolves.Client.Tests.Components.HoldButtonMarkupTests.DesignTokens_AnimateHoldProgressOverProductionDuration` | Deprecated temporary scaffold | Hold progress CSS animation duration stays aligned with production hold duration. | Replace with browser-host computed-style checks for rendered hold progress. |
| `Werewolves.Client.Tests.Components.HoldButtonMarkupTests.Markup_UsesHoldToConfirmResourceString` | Deprecated temporary scaffold | Hold button exposes the hold-to-confirm localized hint. | Replace with ADR-0006/bUnit rendered text/attribute checks. |
| `Werewolves.Client.Tests.Components.HoldButtonMarkupTests.Markup_DeclaresRequiredParameters` | Deprecated temporary scaffold | Hold button keeps its current component API while bUnit coverage is unavailable. | Remove after ADR-0006/bUnit tests instantiate the component through its public parameters. |
| `Werewolves.Client.Tests.Components.HoldButtonMarkupTests.Markup_RendersHoldButtonStructure` | Deprecated temporary scaffold | Hold button renders the structural elements needed for progress and hint styling. | Replace with ADR-0006/bUnit rendered markup checks or browser-host visual checks. |
| `Werewolves.Client.Tests.Components.HoldButtonMarkupTests.Markup_UsesCssStateClassesForVisualFeedback` | Deprecated temporary scaffold | Hold button emits CSS state classes for holding and completion feedback. | Replace with ADR-0006/bUnit rendered state-transition checks. |
| `Werewolves.Client.Tests.Components.HoldButtonMarkupTests.Markup_DisablesButtonWhenDisabledParameterIsTrue` | Deprecated temporary scaffold | Hold button maps its disabled parameter to the rendered button state. | Replace with ADR-0006/bUnit rendered attribute checks. |
| `Werewolves.Client.Tests.Components.SelectPlayersViewMarkupTests.Markup_ContainsPlayerListWithSeatNumberAndName` | Deprecated temporary scaffold | Select-player UI presents selectable Players with seat number and name. | Replace with ADR-0006/bUnit rendered list checks using a real instruction and roster. |
| `Werewolves.Client.Tests.Components.SelectPlayersViewMarkupTests.Markup_ContainsSelectedStateToggle` | Deprecated temporary scaffold | Select-player UI exposes selected state and ARIA selection. | Replace with ADR-0006/bUnit rendered interaction checks. |
| `Werewolves.Client.Tests.Components.SelectPlayersViewMarkupTests.Markup_ContainsPressAndHoldSubmitButton` | Deprecated temporary scaffold | Select-player submission uses the press-and-hold control. | Replace with ADR-0006/bUnit rendered child component and submit-event checks. |
| `Werewolves.Client.Tests.Components.SelectPlayersViewMarkupTests.Markup_PinsSubmitButtonInDashboardActionZone` | Deprecated temporary scaffold | Select-player submit control stays in the dashboard action zone. | Replace with browser-host layout checks or bUnit rendered structure checks. |
| `Werewolves.Client.Tests.Components.SelectPlayersViewMarkupTests.Markup_UsesClientStringsResourceKeys` | Deprecated temporary scaffold | Select-player labels remain resource-backed. | Replace with ADR-0006/bUnit rendered Portuguese text checks. |
| `Werewolves.Client.Tests.Components.SelectPlayersViewMarkupTests.Markup_DeclaresRequiredParameters` | Deprecated temporary scaffold | Select-player component API stays usable by the current dashboard wiring. | Remove after ADR-0006/bUnit tests instantiate the component through public parameters. |
| `Werewolves.Client.Tests.Components.SelectPlayersViewMarkupTests.Markup_SubmitButtonDisabledWhenCannotSubmit` | Deprecated temporary scaffold | Select-player submit is disabled until the current selection can be submitted. | Replace with ADR-0006/bUnit rendered interaction checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_RendersButtonForEachOption` | Deprecated temporary scaffold | Select-options UI renders every option from the instruction. | Replace with ADR-0006/bUnit rendered list checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_UsesSelectedCssClassForHighlighting` | Deprecated temporary scaffold | Select-options UI exposes selected state for visual feedback. | Replace with ADR-0006/bUnit rendered interaction checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_HasSelectionRangeValidation` | Deprecated temporary scaffold | Select-options UI enforces the instruction selection range. | Replace with ADR-0006/bUnit interaction checks and service-level validation where appropriate. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_AcceptsInstructionAndOnResponseParameters` | Deprecated temporary scaffold | Select-options component API stays usable by instruction rendering. | Remove after ADR-0006/bUnit tests instantiate the component through public parameters. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_CallsCreateResponseOnSubmit` | Deprecated temporary scaffold | Select-options submission emits a `ModeratorResponse` from the instruction. | Replace with ADR-0006/bUnit submit-callback checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_SubmitUsesPressAndHoldPattern` | Deprecated temporary scaffold | Select-options submission uses the press-and-hold control. | Replace with ADR-0006/bUnit rendered child component and event checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_PinsSubmitButtonInDashboardActionZone` | Deprecated temporary scaffold | Select-options submit control stays in the dashboard action zone. | Replace with browser-host layout checks or bUnit rendered structure checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_SubmitButtonIsDisabledWhenSelectionInvalid` | Deprecated temporary scaffold | Select-options submit is disabled until the selection is valid. | Replace with ADR-0006/bUnit rendered interaction checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_EnforcesMaximumSelectionCount` | Deprecated temporary scaffold | Select-options UI prevents selection beyond the maximum. | Replace with ADR-0006/bUnit rendered interaction checks. |
| `Werewolves.Client.Tests.Components.SelectOptionsViewTests.Markup_UsesClientStringsResourceKeys` | Deprecated temporary scaffold | Select-options labels remain resource-backed. | Replace with ADR-0006/bUnit rendered Portuguese text checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_RendersRolesFromInstruction` | Deprecated temporary scaffold | Assign-roles UI renders roles from the instruction. | Replace with ADR-0006/bUnit rendered role-list checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_RendersPlayersForAssignment` | Deprecated temporary scaffold | Assign-roles UI renders Players from the instruction. | Replace with ADR-0006/bUnit rendered Player navigation checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_AcceptsInstructionAndOnResponseParameters` | Deprecated temporary scaffold | Assign-roles component API stays usable by instruction rendering. | Remove after ADR-0006/bUnit tests instantiate the component through public parameters. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_AcceptsRosterParameterForPlayerNameResolution` | Deprecated temporary scaffold | Assign-roles receives the roster needed to resolve Player names. | Replace with ADR-0006/bUnit rendered Player-name checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_CallsCreateResponseOnSubmit` | Deprecated temporary scaffold | Assign-roles submission emits a `ModeratorResponse` from the instruction. | Replace with ADR-0006/bUnit submit-callback checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_SubmitUsesPressAndHoldPattern` | Deprecated temporary scaffold | Assign-roles submission uses the press-and-hold control. | Replace with ADR-0006/bUnit rendered child component and event checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_PinsSubmitButtonInDashboardActionZone` | Deprecated temporary scaffold | Assign-roles submit control stays in the dashboard action zone. | Replace with browser-host layout checks or bUnit rendered structure checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_SubmitButtonIsDisabledWhenAssignmentsIncomplete` | Deprecated temporary scaffold | Assign-roles submit is disabled until all assignments are complete. | Replace with ADR-0006/bUnit rendered interaction checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_UsesGetPublicNameForRoleDisplay` | Deprecated temporary scaffold | Assign-roles role labels use the existing public-name localization path. | Replace with ADR-0006/bUnit rendered role-label checks. |
| `Werewolves.Client.Tests.Components.AssignRolesViewTests.Markup_UsesClientStringsResourceKeys` | Deprecated temporary scaffold | Assign-roles labels remain resource-backed. | Replace with ADR-0006/bUnit rendered Portuguese text checks. |
| `Werewolves.Client.Tests.Components.InstructionRendererTests.Markup_HasSelectOptionsInstructionBranch` | Deprecated temporary scaffold | Instruction renderer routes select-options instructions to the options view. | Replace with ADR-0006/bUnit rendered branch checks. |
| `Werewolves.Client.Tests.Components.InstructionRendererTests.Markup_HasAssignRolesInstructionBranch` | Deprecated temporary scaffold | Instruction renderer routes assign-roles instructions to the assign view. | Replace with ADR-0006/bUnit rendered branch checks. |
| `Werewolves.Client.Tests.Components.InstructionRendererTests.Markup_PassesRosterToAssignRolesView` | Deprecated temporary scaffold | Instruction renderer passes roster data to assign-roles rendering. | Replace with ADR-0006/bUnit rendered child-parameter or visible-name checks. |
| `Werewolves.Client.Tests.Components.InstructionRendererMarkupTests.Markup_ContainsSelectPlayersInstructionBranch` | Deprecated temporary scaffold | Instruction renderer routes select-player instructions to the player-selection view. | Replace with ADR-0006/bUnit rendered branch checks. |
| `Werewolves.Client.Tests.Components.InstructionRendererMarkupTests.Markup_PassesRosterParameterToSelectPlayersView` | Deprecated temporary scaffold | Instruction renderer passes roster data to select-player rendering. | Replace with ADR-0006/bUnit rendered child-parameter or visible-name checks. |
| `Werewolves.Client.Tests.Components.InstructionRendererMarkupTests.Markup_DoesNotRenderEmptyFixedActionZoneForInputViews` | Deprecated temporary scaffold | Instruction renderer does not add an extra empty action zone around input views. | Replace with browser-host or bUnit rendered layout checks. |
| `Werewolves.Client.Tests.Components.InstructionRendererMarkupTests.Markup_DeclaresRosterParameter` | Deprecated temporary scaffold | Instruction renderer exposes roster as a current component parameter. | Remove after ADR-0006/bUnit tests instantiate the component through public parameters. |
| `Werewolves.Client.Tests.Components.InstructionRendererHapticTests.InstructionRenderer_UsesTransitionKeyForAnimationReMount` | Deprecated temporary scaffold | Instruction renderer keys instruction blocks so instruction changes restart animation. | Replace with browser-host motion checks or bUnit render-tree evidence that does not freeze field names. |
