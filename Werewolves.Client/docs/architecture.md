# Architecture Requirements Document: Werewolves UI Client

## 1. Project Overview & Philosophy

*   **Goal:** Build a visual "Thin Client" for the `Werewolves` game engine.
*   **Role of the UI:** Render state from Core, collect input, never perform game logic.
*   **Target Platform:** Mobile-First (Android/iOS) via **.NET MAUI Blazor Hybrid**.

## 2. Technology Stack

*   **Framework:** .NET 8/9 MAUI Blazor Hybrid.
*   **UI Component Library:** **MudBlazor**.
    *   *Constraint:* **Minimal Custom CSS.**
    *   *Rule:* Use MudBlazor utility classes (`d-flex`, `pa-4`) for 95% of styling.
    *   *Exception:* Custom CSS is permitted strictly for layout shims (e.g., `touch-action`, Safe Area Insets, Scroll Locking) where utility classes fail.
*   **Audio:** `Plugin.Maui.Audio`.
    *   *Asset Location:* `Resources/Raw/Audio` (Maui Assets).
    *   *Capability:* **Background Audio** must be enabled in Android Manifest / iOS Info.plist.
*   **Device Control:** `Microsoft.Maui.Devices.IDeviceDisplay` (Screen Wake Lock).

## 3. Architecture Pattern: Model-View-Adapter (MVA)

### 3.1. The Model (Core)
*   `GameSession` from `Werewolves.StateModels`. Immutable from client perspective.

### 3.2. The View (Blazor Components)
*   **State Rule:** No duplication of game state.
*   **Transient State:**
    *   UI-specific state (Draft inputs, Accordion open/close) lives in the Component.
    *   **Persistence Note:** This "Draft State" is **ephemeral**. If the App crashes or restarts during input (e.g. Assigning Roles), draft state is lost.
*       **IDisposable Implementation**: Any component subscribing to StateChanged MUST implement IDisposable and unsubscribe in the Dispose() method.
*   **Navigation:** `MudTabs` with `KeepPanelsAlive="true"`.
    *   **State Refresh Risk:** Because panels remain alive, they may not automatically re-render when hidden.
    *   **Solution:** Components must subscribe to `GameClientManager.StateChanged` or use a `CascadingParameter` to force `StateHasChanged()` when the Session Core Object reference updates.

### 3.3. The Adapter (GameClientManager)
*   Proxies input, holds active session.
*   **Audio Ownership:** The `GameClientManager` (Singleton) holds the reference to the active `IAudioPlayer`. It is responsible for starting/stopping/looping tracks. The View only sends signals (e.g. "Mute Toggled").
*   **Persistence Authority:** Handles writing state to disk immediately upon mutation.

## 4. The Bridge: `GameClientManager` Contract

*   Standard contract as defined previously.
*   **Persistence:**
    *   Trigger: **Immediate write** on successful `ProcessInput()`.
    *   Location: `FileSystem.AppDataDirectory`.

## 5. UI Design & Behavior

### 5.1. Navigation & Layout
*   **Tab System:** `MudTabs` with `KeepPanelsAlive="true"`.
    *   *Benefit:* This ensures the **Auto-Timer** (in the Action Tab) continues ticking even if the user views the Stats Tab.

### 5.2. Component Specifics

#### A. Roster Tab
*   Standard list.

#### B. Action Tab (Instruction Renderer & Audio)
*   **Audio Logic (Singleton Managed):**
    *   **Default Behavior:** All tracks are treated as **Looping** (Ambience/Music) for V1. One-shot SFX are out of scope for V1.
    *   **Reconciliation:** On `App.OnResume` or `StateChanged`, the Manager checks if the correct audio for the current Instruction is playing. If not, it swaps tracks.
*   **Timer:** A local UI timer. Resets on component initialization (new instruction).

#### C. Input Views
*   **Submission:** `GameClientManager.ProcessInput`.
*   **`AssignRolesView`:**
    *   **Form Factor:** Single Page Form.
    *   **Draft Data:** `Dictionary<Guid, string?>` (PlayerId -> RoleId).
    *   **Logic:** Local "Inventory" logic tracks role counts.
    *   **Validation:** Prevents submission until `AssignedRoles` matches `AvailableRoles`.     
        *   **Validation Feedback**: The view must calculate the delta (Required vs. Assigned) and display a specific warning message (e.g., "Need 1 more Villager") near the disabled Submit button.

#### D. Lobby (Setup)
*   **Reordering:**
    *   **Primary:** "Move Up" / "Move Down" Buttons.
    *   **Secondary:** `MudDropZone`.
    *   *CSS Note:* Use `touch-action: none` on drag handles. Drag handles must be explicit, distinct icons (e.g., hamburger menu icon) on the right side of the row. The rest of the row must remain scrollable.

### 5.3. Lifecycle & Persistence
*   **Wake Lock:** Active during **Lobby** AND **Dashboard**.
*   **Persistence Strategy:** 
    *   **Save:** Immediately after `GameClientManager` receives a new State from Core (post-input).
    *   **Load:** On App Start / `App.OnResume`.

## 6. Project Structure

```text
Werewolves.Client/
├── MauiProgram.cs              # DI, Background Audio Config
├── Resources/
│   ├── Raw/
│   │   └── Audio/              # .mp3/.wav assets
│   └── Images/                 # Role Icons / Status Effects
├── Services/
│   ├── GameClientManager.cs    # Holds Session AND IAudioPlayer
│   ├── AudioMap.cs             # Maps Instruction.Id -> Audio Filename
│   └── ImageMap.cs             # Maps Role.Id -> Image Filename
├── Components/
│   ├── Layout/
│   │   └── MainLayout.razor
│   ├── Pages/
│   │   ├── Lobby.razor
│   │   └── Dashboard.razor     # MudTabs(KeepPanelsAlive=true)
│   └── Game/
│       ├── InstructionRenderer.razor
│       ├── Views/
│       │   ├── AssignRolesView.razor       # Single Page Form
│       │   ├── SelectPlayersView.razor
│       │   ├── SelectOptionsView.razor
│       │   └── ConfirmationView.razor
│       └── DashboardTabs/
└── wwwroot/
    └── css/
        └── app.css             # Minimal layout shims
```

## 7. Implementation Guidelines

*    **MudBlazor First**: Use MudBlazor components.
*    **CSS**: Only write CSS if MudBlazor classes cannot solve a layout glitch (e.g., Safe Areas).
*    **CSS Units**: Use Standard CSS units (px, rem). The WebView Viewport tag handles the translation to Device Independent Pixels (dp).
*    **Reactivity**: Components must explicitly handle state refreshes when ActiveSession changes to support KeepPanelsAlive.
*    **Error Handling**: Use Snackbars for logic errors, Crash for exceptions.        