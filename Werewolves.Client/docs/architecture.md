# Architecture: Werewolves UI Client

## 1. Philosophy

*   **Goal:** Thin client for the `Werewolves` game engine.
*   **Role of the UI:** Render state from Core, collect input, never perform game logic.
*   **Target Platform:** Mobile-first (Android/iOS) via .NET MAUI Blazor Hybrid. The browser QA host is local tooling, not a public web product surface.
*   **Two-Tempo Model:** The UI serves two distinct usage modes on the same device:
    *   **High-intensity (bursty):** Phase transitions, role wake-ups, vote outcomes. The moderator glances at the phone, acts, and returns attention to the table. UI must be streamlined, consistent, and operable with minimal attention.
    *   **Low-intensity (calm):** Debate, setup. The moderator has time and attention to browse state, review the roster, and plan. Extra information surfaces are appropriate here.
*   **Client displays only what the Core provides.** The client must not maintain its own player lists, infer game state, or supplement Core data. It renders exactly what it receives and submits only options afforded to it by the Core.

## 2. Technology Stack

*   **Framework:** .NET 10 MAUI Blazor Hybrid shell plus a host-agnostic Razor Class Library (`Werewolves.Client.Shared`) for the shared Moderator UI. A separate `Werewolves.Client.BrowserQaHost` ASP.NET Core Blazor host runs locally for browser inspection/debug only.
*   **UI Components:** Shared Blazor components styled through RCL static web assets in `Werewolves.Client.Shared/wwwroot/css/`.
    *   Prefer semantic component classes and CSS variables with the `--ww-*` token set.
    *   Shared behavior such as tabs, expansion panels, dialogs, and notifications should live behind thin app components/services rather than a comprehensive third-party design system.
    *   Custom CSS is expected for layout, touch behavior, safe area insets, and the moderator-specific interaction patterns.
*   **Mobile Audio:** `Plugin.Maui.Audio` behind the shared `IAudioAssetLoader`, `IAudioPlayerFactory`, and `IInstructionAudioPlayback` contracts.
    *   Asset location: `Resources/Raw/Audio`.
    *   Background audio must be enabled in Android Manifest / iOS Info.plist.
    *   Sound effect triggers are owned by the Core (see ADR-0001). The client resolves semantic identifiers to audio files and handles playback.
*   **Device Control:** `Microsoft.Maui.Devices.IDeviceDisplay` (Screen Wake Lock) behind the shared `IScreenWakeLock` contract.
*   **Haptics:** MAUI haptic feedback behind the shared `IHapticFeedbackService` contract.
*   **Theme:** Single dark theme. No dynamic switching.
*   **Localization:** Portuguese for v1. Core uses `GameStrings.resx`; the shared client RCL maintains `.resx` files for UI-only strings (button labels, validation messages, prompts).
*   **Native device QA:** Release/device checks for audio output, haptic feel, wake lock behavior, resume/background behavior, platform storage behavior, packaging/install behavior, native WebView rendering quirks, and touch feel live in `docs/native-device-qa-checklist.md`.

## 3. Architecture Pattern: Model-View-Adapter (MVA)

### 3.1. The Model (Core)
*   `GameSession` from `Werewolves.Core.StateModels`. Immutable from client perspective.
*   The client accesses game state exclusively through `IGameSession` and receives directives via `ModeratorInstruction`.

### 3.2. The View (Blazor Components)
*   **Shared boundary:** Moderator pages, routes, input views, client resources, and shared CSS live in `Werewolves.Client.Shared` and target `net10.0`.
*   **Host-agnostic:** Components can render without MAUI, device APIs, app-package assets, or a real filesystem. Native behavior enters through injected contracts only.
*   **No duplication of game state.** Components never cache or shadow Core state.
*   **Transient State:** UI-specific state (draft selections, accordion state) lives in the component. This draft state is ephemeral — lost on app crash during input.
*   **IDisposable:** Any component subscribing to `StateChanged` must implement `IDisposable` and unsubscribe in `Dispose()`.
*   **Navigation:** App-owned tab component that keeps panels alive while hidden.
    *   Panels remain alive when hidden; components must subscribe to `GameClientManager.StateChanged` to refresh when the active session updates.

### 3.3. The Adapter (GameClientManager)
*   Singleton in each host. Proxies moderator input to Core, holds the active session, coordinates audio playback, and handles persistence through host-safe interfaces.
*   Monolithic for v1 — audio, persistence, and session management live in one class. The seams for future decomposition are clear (audio, persistence, session) but splitting is deferred until complexity warrants it.
*   **Audio:** Coordinates `IInstructionAudioPlayback`. The shared implementation maps Core sound effects and delegates stream loading/player creation to host adapters. The mobile host uses app-package audio files and `Plugin.Maui.Audio`; tests use no-op or fake services.
*   **Persistence:** Requests the Core-owned stable Game Session export through `GameService.SerializeSession` using the active session ID, wraps it in the client-owned recovery envelope, and writes it through `IGameSessionSaveStore`. The initial Lobby Exit export and save remain part of the transactional pre-publication boundary; export-and-save attempts after successful `ProcessInput()` are best-effort. The export represents only the latest validated stable recovery snapshot, not the live execution tail. The mobile host injects `FileGameSessionSaveStore` rooted at `FileSystem.AppDataDirectory`; shared UI tests and the browser QA host can inject in-memory or disabled storage.
*   **Recent setups:** `IRecentSetupStore` is a second, independent store containing only ordered Player names, normalized Role counts, and capture times. A successful Lobby Exit captures through this seam only after the blocking start/recovery/publication boundary has completed, and that final capture is best-effort. Loading a recent setup reuses the existing atomic Lobby decision/persistence/publication boundary with fresh Player identities and no accepted artifacts; deletion commits the recent-setups store before the Landing row is removed. The native file store uses its own current-schema payload and atomic file, never the Game Session recovery slot.
*   **Recoverable Lobby:** Every public recoverable Lobby operation is coordinated through `LobbySetupState.Decide(LobbyChange)`, its `Keep`/`Clear`/`Replace` persistence instruction, opaque complete-aggregate publication, and one staged-recovery, evaluation, and notification reconciliation path. Staged-Lobby Rehydration is a separate input path: it decodes and validates one complete current-schema aggregate before visibility, publishes it once through the same assignment-only boundary, and only then reconciles staged recovery memory; it neither enters `LobbyChange` nor re-saves the decoded payload. Lobby Exit remains separate and irreversible: it persists the active Game Session before client publication and finalizes the latest accepted Role Lock-In.

### 3.4. Browser QA Host
*   `Werewolves.Client.BrowserQaHost` is a local-only ASP.NET Core Blazor composition root for browser inspection, screenshots, DOM inspection, viewport checks, and representative interactions.
*   It mounts the shared `Routes` root from `Werewolves.Client.Shared`; it does not own copied web pages for lobby, role selection, dashboard, instruction, or victory flows.
*   It registers browser-safe substitutes for native-only contracts: no-op haptics, no-op wake lock effect, disabled/no-op audio playback, and in-memory local QA save storage.
*   It sets `pt-PT` culture and keeps Moderator-visible copy resource-backed through the same shared resources as the mobile app.
*   Query fixtures such as `/?qa=lobby`, `/?qa=dashboard`, and `/?qa=victory` seed shared services and Core public APIs for inspection only. They are not product navigation, accounts, multiplayer, production persistence, or a required CI gate.

## 4. Navigation & Layout

### 4.1. Pages
*   **Landing:** Cold-process launch hub. It renders before Lobby, Dashboard, or Victory, exposes Continue only for a recovered active or finished Game Session, and guards New Game Session with confirmation when abandonment is required. New Game Session enters the current in-process Lobby without resetting it; the Roster can return to Landing while preserving that Lobby. Ordinary foreground resume keeps the current surface, and Dashboard/Victory return-to-Lobby continues to render the Roster directly.
*   **Lobby:** Game setup. Roster definition and Role Composition selection remain the first two steps. Conditional configuration then appears only when required: Actor Setup Cards when Actor is reachable from the committed setup, and the public two-group partition when Prejudiced Manipulator is reachable. These are lobby inputs, not Core Moderator Instructions; the client records the Moderator-created physical setup and never generates cards or balances groups. The current client preserves completed inputs on back-navigation. Before Lobby Exit, accepted Role Lock-In and Actor Setup Card artifacts remain replaceable; an equivalent replacement retains exact-current Safety-Screening, while a changed Canonical Simulation Scenario invalidates it. Navigation reaches Dashboard only after the applicable configuration is valid and `GameSessionConfig` is fulfilled. The landed Thief-specific Role Lock-In, partition, branch-screening, and card-zone flow is described below.
*   **Dashboard:** Gameplay. Three tabs — Roster, Action, Stats.

### 4.2. Tab Bar
*   Always visible, positioned at the top of the screen.
*   Action controls (buttons, inputs) positioned at the bottom in the thumb zone.
*   Spatial separation between tabs (top) and action zone (bottom) prevents accidental tab switches during high-intensity moments.

### 4.3. Phase & Turn Indicator
*   Persistent bar visible across all tabs and pages showing current phase and turn number (e.g., "Night 3", "Day 2").

## 5. Instruction Rendering

### 5.1. Single-Screen Presentation
*   `InstructionRenderer` owns one presentation seam that classifies each current `ModeratorInstruction` family once for view routing, initial guidance expansion, and response-surface selection.
*   Public and Moderator-private guidance render on the same screen. Public guidance remains visually dominant; private guidance remains subordinate. There is no public-to-private navigation step.
*   For data-entry instructions with both guidance blocks, public guidance starts expanded and private guidance starts as a first-line preview. Expanding the collapsed block makes it the sole expanded block; response controls remain available throughout.
*   For passive instructions, every present guidance block starts expanded and can be collapsed or expanded independently.
*   A public-only or private-only instruction renders its one available block directly without a synthetic counterpart.

### 5.2. Transitions Between Instructions
*   In-place transition: quick fade or right-to-left swipe animation.
*   Haptic feedback fires on the game-progressing interaction itself—tap or successful hold—not on the transition animation.
*   Instruction-guidance expansion retains its established lightweight click haptic. Other general UI interactions (dropdowns, tab switches) do not add haptic feedback.

### 5.3. Submission Behavior
*   One-way Continue acknowledgments use a localized Continue control and submit `ExpectedInputType.Continue`, never a Boolean choice.
*   The current `ConfirmationView` retains press-and-hold because baseline confirmations include irreversible physical/public events. Vote and option submissions use the same commitment gate; a canceled or incomplete hold emits no response.

### 5.4. Timer
*   Count-up stopwatch. Resets on new instruction. Runs independently of tab focus (panels stay alive).

## 6. Input Views

### 6.1. SelectPlayersView
*   Vertical list in seating order.
*   Only displays players provided by the Core's `SelectablePlayerIds` — no supplementary player data.
*   Tap to select/deselect. Submit enabled when selection meets `CountConstraint`.

### 6.2. SelectOptionsView
*   Vertical list in the Core-provided semantic order. Render each option's localized label, but track and submit only its machine-stable ID; duplicate labels remain distinct choices.
*   Tap to select, then press and hold to submit.
*   The Thief flow renders only Core-provided, machine-stable `Offer1`, `Offer2`, and `Decline` options, with `Decline` absent when illegal. The client neither infers legality nor recreates the locked private offer pair.

### 6.3. AssignRolesView
*   Used during gameplay when a role is revealed (elimination, not setup).
*   Renders every requested Player from the Core-provided `SelectableRolesForPlayers` map. A one-distinct-Role multiset is a named confirmation with no picker; a multi-option Player gets a printed-Role picker, and only the Core-provided `PlayersForAssignment` set is submitted as explicit mappings.

### 6.4. ConfirmationView
*   Single localized "Continue" press-and-hold control that emits the instruction's one-way `ExpectedInputType.Continue` response after the hold completes.

### 6.5. Devoted Servant Vote-Reaction Flow
*   The public pre-reveal window is one narrow Core-owned instruction that accepts either the existing correlated Continue payload or the existing exact-one Player-selection payload for a public self-reveal. The client does not encode no-use as an empty selection, semantic Decline, or localized Use option.
*   After an accepted self-reveal, the renderer shows only the correlated private printed-Role recording instruction for the already-fixed Vote Target. It never asks the Moderator to choose the target or a physical card ID.
*   The acquired Role is visible only to the Servant at the table and to Moderator-private client projections. Public history and public roster projections expose the former Devoted Servant reveal and discard, not the acquired Role.

## 7. Dashboard Tabs

### 7.1. Roster Tab
*   Player list showing role, health (alive/dead), and status effects.
*   This is a Moderator-only surface: it may show legitimately learned private state, but it does not imply that Players publicly know those Roles. PRD #93/#113 separates unknown, Moderator-known, and publicly revealed Role state; public roster/history projections use only public knowledge.

### 7.2. Action Tab
*   Renders the current `ModeratorInstruction` through the single-screen presentation seam.
*   Houses the count-up timer.
*   Audio controls (mute/unmute).

### 7.3. Stats Tab
*   Roles remaining per faction (list/table).
*   Elimination log (chronological).
*   Win probability guidance is not a current product surface. Richer simulator work is parked in PRD #94 without a near-term delivery commitment.

## 8. Lobby

### 8.1. Step 1: Roster Definition
*   Text input to add player names.
*   Reordering via "Move Up" / "Move Down" buttons.
*   "Next" button navigates to role selection.

### 8.2. Step 2: Role Selection
*   Roles grouped by Role Group (Villagers, Werewolves, Ambiguous, Loners).
*   Stepper control (+/-) per role for count.
*   Persistent summary bar: `Selected: X/Y` (current vs. target based on player count, or player count plus two in a Thief-enabled setup).
*   Submit disabled until count matches. Thief validation also requires a Player-count Deal Pool with exactly one Thief, neither offer may print Thief, and two different offer-instance identities even when their printed Roles match; errors appear inline.
*   Back-navigation preserves selections.

### 8.3. Thief Role Lock-In And Physical Flow
*   For `P` Players, a Thief-enabled Role Composition contains `P + 2` physical Character Card instances. At Role Lock-In, the Moderator partitions it into a `P`-card Deal Pool containing exactly one Thief and two named, private Thief Offer Card instances. `Offer1` and `Offer2` must be distinct physical non-Thief instances, even when they print the same Role.
*   Role Lock-In does not perform the Physical Deal or exit the Lobby. It derives every conditional setup stage required by any Role in the Deal Pool or either offer, so an offered Actor still requires Actor Setup Cards and an offered Prejudiced Manipulator still requires the Public Group Partition. Before Lobby Exit, the Moderator may replace the accepted Role Lock-In; exact-current Safety-Screening continues when the replacement has the same Canonical Simulation Scenario, and only a changed identity invalidates the previous request or result.
*   The committed Deal Pool/offer partition is part of Canonical Simulation Scenario identity, and pre-game Already-Decided classification reads initial coverage from the Deal Pool rather than offer-only Roles. Safety screening evaluates every semantically distinct legal Night 1 branch: `Offer1`, `Offer2`, and `Decline` when legal. Same-printed-Role offers keep distinct physical identities but may share one behaviorally identical screening branch. Any Degenerate branch blocks Lobby Exit. If none is Degenerate, all completed non-degenerate branches pass; failures, timeouts, or other incomplete branches yield Could Not Evaluate without blocking Lobby Exit.
*   Only the Deal Pool is shuffled and physically dealt, guaranteeing exactly one initial Thief holder while leaving Player-specific ownership unknown to the app. The offers remain in their private Thief Offer zone for the Night 1 instruction.
*   The private Thief input exposes machine-stable `Offer1`, `Offer2`, and legal `Decline` options. Choosing an offer atomically makes that instance Player-owned, makes its printed Role current with fresh power state, and moves the original Thief card plus the unchosen offer to the private Set-Aside zone. Declining keeps the Thief card and current Role and moves both offers to the private Set-Aside zone. A successful response creates the stable checkpoint containing those results and the pending sleep instruction before Core returns success. The flow then continues through the remaining canonical Night 1 calls without replaying setup or an earlier call.

### 8.4. Production Lobby Safety Evaluation
*   The production client retains two pre-game safety gates: deterministic Already-Decided Role Composition detection and 1,000-run Degenerate Simulation Scenario screening.
*   Either safety classification blocks lobby exit and explains the actionable setup problem to the Moderator.
*   Under ADR-0013, production stops after the 1,000-run degenerate-screening gate. It neither requests the 10,000-run probability batch nor presents Game Result Frequency or Ended-By-Turn Frequency.
*   Production consults only local terminal records whose complete identity exactly matches the requested current capability. A missing, stale, or foreign record starts bounded on-device evaluation.
*   The full probability path remains dormant for possible future work; it is not deleted or interpreted as current balance guidance. Production packages no simulator cache, and pre-release `core-simulator@1` records never project into Safety Screening.

## 9. Lifecycle

*   **Wake Lock:** Active during Lobby and Dashboard.
*   **Persistence:** Attempt to save after each successful `ProcessInput()`. On cold process launch, recover the single-slot payload before the Landing surface is shown: a staged Lobby is decoded, validated, and published eagerly, while an active or finished Game Session makes Continue available without navigating into it. Empty or unreadable recovery produces no Continue action. Ordinary `App.OnResume` preserves the current in-process surface and state.
*   **Stable recovery boundary:** A save attempt does not imply durable game progress advanced. `GameService.SerializeSession`, called with the active session ID, returns the Core's latest validated stable recovery snapshot, so current-phase tail work remains volatile until Core captures a new boundary.
*   **Transient state is not serialized** (see ADR-0002). On process kill and Rehydration, active sub-phase stage, active listener, and listener state are discarded; the game resumes from the committed boundary instruction and minimal phase cursor.
*   **Committed response checkpoints:** A successful Thief choice or decline creates a narrow stable checkpoint atomically with its state transition and pending sleep instruction, so an already completed exchange is never requested or applied twice. An accepted Devoted Servant self-reveal similarly resumes only at the private printed-Role record, and an accepted swap resumes the same Vote Target's resolution; neither checkpoint claims arbitrary live-listener serialization.
*   **Crash-safe write behavior:** `FileGameSessionSaveStore` writes the new payload to a temporary file in the save directory, then replaces or renames it into place. If platform atomic replacement is unavailable, same-directory rename overwrite is the accepted fallback. Stale temporary write artifacts are cleaned up on save and clear where practical.

## 10. Error Handling

*   **Lobby:** Inline validation messages near the relevant section. No snackbars for predictable validation failures.
*   **Gameplay:** App-owned toast/snackbar notifications for runtime errors (e.g., `ProcessResult.IsSuccess == false`).

## 11. Project Structure

*   `Werewolves.Client.Shared/Components/` contains shared pages, routes, and game input views.
*   `Werewolves.Client.Shared/Services/` contains host-safe services and contracts such as `GameClientManager`, `AudioMap`, wake-lock/haptics abstractions, persistence contracts, and lobby/dashboard projections.
*   `Werewolves.Client.Shared/Resources/` contains client UI localization strings.
*   `Werewolves.Client.Shared/wwwroot/css/` contains shared design tokens and app CSS served as RCL static web assets.
*   `Werewolves.Client/` remains the MAUI shell, native service composition root, platform metadata owner, and `BlazorWebView` host for the shared `Routes` component.
*   `Werewolves.Client.BrowserQaHost/` is the local browser QA composition root for the shared `Routes` component and browser-safe service adapters.
*   `Werewolves.Client/Resources/Raw/Audio/` contains native audio assets loaded by the mobile host.
*   `Werewolves.Client/Services/` contains native adapters such as file persistence and `Plugin.Maui.Audio` player creation.
*   `Werewolves.Client.Tests/Helpers/ModeratorComponentTestContext.cs` is the bUnit fixture pattern: set `pt-PT` culture, use `ClientStrings`, register fake/no-op host services, and render shared components through the RCL reference.
