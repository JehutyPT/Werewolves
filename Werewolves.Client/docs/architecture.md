# Architecture: Werewolves UI Client

## 1. Philosophy

*   **Goal:** Thin client for the `Werewolves` game engine.
*   **Role of the UI:** Render state from Core, collect input, never perform game logic.
*   **Target Platform:** Mobile-first (Android/iOS) via .NET MAUI Blazor Hybrid.
*   **Two-Tempo Model:** The UI serves two distinct usage modes on the same device:
    *   **High-intensity (bursty):** Phase transitions, role wake-ups, vote outcomes. The moderator glances at the phone, acts, and returns attention to the table. UI must be streamlined, consistent, and operable with minimal attention.
    *   **Low-intensity (calm):** Debate, setup. The moderator has time and attention to browse state, review the roster, and plan. Extra information surfaces are appropriate here.
*   **Client displays only what the Core provides.** The client must not maintain its own player lists, infer game state, or supplement Core data. It renders exactly what it receives and submits only options afforded to it by the Core.

## 2. Technology Stack

*   **Framework:** .NET 10 MAUI Blazor Hybrid.
*   **UI Components:** App-owned Blazor components styled through `wwwroot/css/design-tokens.css`.
    *   Prefer semantic component classes and CSS variables with the `--ww-*` token set.
    *   Shared behavior such as tabs, expansion panels, dialogs, and notifications should live behind thin app components/services rather than a comprehensive third-party design system.
    *   Custom CSS is expected for layout, touch behavior, safe area insets, and the moderator-specific interaction patterns.
*   **Audio:** `Plugin.Maui.Audio`.
    *   Asset location: `Resources/Raw/Audio`.
    *   Background audio must be enabled in Android Manifest / iOS Info.plist.
    *   Sound effect triggers are owned by the Core (see ADR-0001). The client resolves semantic identifiers to audio files and handles playback.
*   **Device Control:** `Microsoft.Maui.Devices.IDeviceDisplay` (Screen Wake Lock).
*   **Theme:** Single dark theme. No dynamic switching.
*   **Localization:** Portuguese for v1. Core uses `GameStrings.resx`; client maintains its own `.resx` for UI-only strings (button labels, validation messages, prompts).

## 3. Architecture Pattern: Model-View-Adapter (MVA)

### 3.1. The Model (Core)
*   `GameSession` from `Werewolves.Core.StateModels`. Immutable from client perspective.
*   The client accesses game state exclusively through `IGameSession` and receives directives via `ModeratorInstruction`.

### 3.2. The View (Blazor Components)
*   **No duplication of game state.** Components never cache or shadow Core state.
*   **Transient State:** UI-specific state (draft selections, accordion state) lives in the component. This draft state is ephemeral — lost on app crash during input.
*   **IDisposable:** Any component subscribing to `StateChanged` must implement `IDisposable` and unsubscribe in `Dispose()`.
*   **Navigation:** App-owned tab component that keeps panels alive while hidden.
    *   Panels remain alive when hidden; components must subscribe to `GameClientManager.StateChanged` to refresh when the active session updates.

### 3.3. The Adapter (GameClientManager)
*   Singleton. Proxies moderator input to Core, holds the active session, manages audio playback, and handles persistence.
*   Monolithic for v1 — audio, persistence, and session management live in one class. The seams for future decomposition are obvious (audio, persistence, session) but splitting is deferred until complexity warrants it.
*   **Audio:** Holds the active `IAudioPlayer`. Responsible for starting/stopping/looping tracks. Reconciles on `App.OnResume` or `StateChanged`. The View only sends signals (e.g., "Mute Toggled").
*   **Persistence:** Attempts a write to `FileSystem.AppDataDirectory` after successful `ProcessInput()`, but the Core payload represents only the latest stable Main Phase recovery boundary. Single active session — one save file, replaced on each save attempt.

## 4. Navigation & Layout

### 4.1. Pages
*   **Lobby:** Game setup. Two-step wizard — roster definition first, then role selection. Seamless back-navigation preserves role selections when returning to roster. Navigates to Dashboard once `GameSessionConfig` is fulfilled and the game is created.
*   **Dashboard:** Gameplay. Three tabs — Roster, Action, Stats.

### 4.2. Tab Bar
*   Always visible, positioned at the top of the screen.
*   Action controls (buttons, inputs) positioned at the bottom in the thumb zone.
*   Spatial separation between tabs (top) and action zone (bottom) prevents accidental tab switches during high-intensity moments.

### 4.3. Phase & Turn Indicator
*   Persistent bar visible across all tabs and pages showing current phase and turn number (e.g., "Night 3", "Day 2").

## 5. Instruction Rendering

### 5.1. Two-Part Flow
*   When a `ModeratorInstruction` has both `PublicAnnouncement` and `PrivateInstruction`:
    1. **First screen:** Public text only (the moderator reads this aloud).
    2. **Second screen (after tap):** Private text + input controls.
*   When only one text field is present (public or private), show it directly with the input controls. No extra tap.

### 5.2. Transitions Between Instructions
*   In-place transition: quick fade or right-to-left swipe animation.
*   Haptic feedback fires on the navigation tap itself (not on the transition animation).
*   Haptic is limited to taps that progress the game — not on general UI interactions (dropdowns, tab switches).

### 5.3. Submission Behavior
*   **Game input** (`SelectPlayersInstruction`, `SelectOptionsInstruction`, `AssignRolesInstruction`): Press-and-hold to submit (~0.5-1s). Prevents accidental commitment of game-altering decisions.
*   **Simple confirmations** (`ConfirmationInstruction`): Standard tap. These are acknowledgments, not decisions.

### 5.4. Timer
*   Count-up stopwatch. Resets on new instruction. Runs independently of tab focus (panels stay alive).

## 6. Input Views

### 6.1. SelectPlayersView
*   Vertical list in seating order.
*   Only displays players provided by the Core's `SelectablePlayerIds` — no supplementary player data.
*   Tap to select/deselect. Submit enabled when selection meets `CountConstraint`.

### 6.2. SelectOptionsView
*   Vertical list of options. Tap to select.

### 6.3. AssignRolesView
*   Used during gameplay when a role is revealed (elimination, not setup).
*   Typically one player at a time — a simple role picker from `RolesForAssignment` (unassigned roles).

### 6.4. ConfirmationView
*   Single "Proceed" button. Standard tap.

## 7. Dashboard Tabs

### 7.1. Roster Tab
*   Player list showing role, health (alive/dead), and status effects.
*   All information visible — no hidden roles. The moderator assigned the cards and learns roles during play; the app surfaces everything it knows.

### 7.2. Action Tab
*   Renders the current `ModeratorInstruction` via the two-part flow.
*   Houses the count-up timer.
*   Audio controls (mute/unmute).

### 7.3. Stats Tab
*   Roles remaining per faction (list/table).
*   Elimination log (chronological).
*   Win probability calculator: deferred, near-term future feature.

## 8. Lobby

### 8.1. Step 1: Roster Definition
*   Text input to add player names.
*   Reordering via "Move Up" / "Move Down" buttons.
*   "Next" button navigates to role selection.

### 8.2. Step 2: Role Selection
*   Roles grouped by Role Group (Villagers, Werewolves, Ambiguous, Loners).
*   Stepper control (+/-) per role for count.
*   Persistent summary bar: `Selected: X/Y` (current vs. target based on player count).
*   Submit disabled until count matches. Structural validation errors (e.g., Thief +2 rule) shown as inline messages.
*   Back-navigation preserves selections.

## 9. Lifecycle

*   **Wake Lock:** Active during Lobby and Dashboard.
*   **Persistence:** Attempt to save after each successful `ProcessInput()`. Load on app start / `App.OnResume`. If a save file exists on launch, resume; otherwise show Lobby.
*   **Stable recovery boundary:** A save attempt does not imply durable game progress advanced. `IGameSession.Serialize()` returns the Core's latest stable Main Phase recovery snapshot, so current-phase tail work remains volatile until Core captures a new boundary.
*   **Transient state is not serialized** (see ADR-0002). On process kill and Rehydration, active sub-phase stage, active listener, and listener state are discarded; the game resumes from the committed boundary instruction and minimal phase cursor.
*   **Crash-safe write behavior:** `FileGameSessionSaveStore` writes the new payload to a temporary file in the save directory, then replaces or renames it into place. If platform atomic replacement is unavailable, same-directory rename overwrite is the accepted fallback. Stale temporary write artifacts are cleaned up on save and clear where practical.

## 10. Error Handling

*   **Lobby:** Inline validation messages near the relevant section. No snackbars for predictable validation failures.
*   **Gameplay:** App-owned toast/snackbar notifications for runtime errors (e.g., `ProcessResult.IsSuccess == false`).

## 11. Project Structure

*   Components live under `Components/` — pages, layout, game views, and dashboard tabs.
*   Services live under `Services/` — `GameClientManager`, `AudioMap`, `ImageMap`.
*   Client-specific localization strings live under `Resources/`.
*   Audio assets live under `Resources/Raw/Audio/`.
*   Minimal layout shim CSS lives under `wwwroot/css/`.
