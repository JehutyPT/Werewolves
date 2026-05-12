### Updated File 2: `Werewolves.Client/Documentation/UI-client-requirements.md`

```markdown
# UI Client requirements

### 1. Architecture & Core Principles
*   **Form Factor:** Mobile-first, touch-centric.
*   **Units:** All dimensions must be specified in `dp` (Device Independent Pixels), implemented via CSS `px` and standard Viewport meta tags.
*   **Philosophy:** "Dumb Terminal." The UI does not calculate game state.
*   **Navigation Paradigm:** "Carousel" Navigation (Tabs).
*   **State Authority:** The Core guarantees seating order; UI renders linearly.

---

### 2. View 1: The Lobby (Setup)
*   **System Behavior:**
    *   **Wake Lock:** Screen must remain on during setup.
*   **Roster Management:**
    *   Text input to add names.
    *   **Ordering:**
        *   **Primary:** Manual "Move Up" / "Move Down" buttons.
        *   **Secondary:** Drag-and-Drop (Progressive Enhancement).
*   **Role Configuration:**
    *   List of available roles (fetched from Core metadata).
    *   **Visuals:** Must display Role Icons mapped via `ImageMap` service.
*   **Start Game Trigger:** 
    *   Attempts to instantiate `GameSessionConfig`.

---

### 3. View 2: Gameplay - The Dashboard (The 3 Tabs)

#### Tab A: Player List (Left)
*   **Layout:** Vertical list.
*   **Item Appearance:** Alive (Normal) vs Dead (Greyed out).
*   **Interaction:** Tap to expand details (Role Icon/Status Effects).

#### Tab B: Pending Instruction (Center - Default)
*   **Header:**
    *   **Auto-Timer:** A generic timer (MM:SS).
    *   *Behavior:* Persists while switching tabs.
*   **Body:** Renders the specific **Instruction Handler**.
*   **Footer/Context:**
    *   **Audio Control:** Buttons to Mute/Unmute.
    *   **Audio Logic:** Audio is owned by the Session Manager (Singleton) and Loops by default.

#### Tab C: Game Overview (Right)
*   **Stats:** Current Phase, Turn Count, Game Data.

---

### 4. Instruction Handlers (The "Pending Instruction" Tab Logic)

**General UI Elements:**
*   **Public Announcement:** Large text.
*   **Private Note:** Italicized text.

#### A. `AssignRolesInstruction` Handler
*   **UX Flow:** **Single Page Form**.
*   **Data Requirement:** Uses `AssignRolesDraftModel` (Dictionary) to track transient selections.
*   **Validation:**
    *   "Submit" button disabled until inventory is perfectly balanced.
    *   **Edge Case:** If app crashes during assignment, draft selection is lost.

#### B. `SelectPlayersInstruction` Handler
*   **Layout:** Reuses Player List component.
*   **Validation:** "Confirm" button enabled only when `SelectionCount` meets Min/Max.

#### C. `SelectOptionsInstruction` Handler
*   **Layout:** Vertical list of Radio buttons or Checkboxes.

#### D. `ConfirmationInstruction`
*   **Action:** Single "Proceed" button.

---

### 5. Required Updates to Core Library
1.  **`GameSessionConfig`:** validation logic exposed to UI.
2.  **Persistence:** Serialize/Rehydrate implementations.

### 6. Persistence & Reliability
*   **Strategy:** Write-on-Update.
*   **Trigger:** The client must save the game state to disk immediately after successfully processing user input (before the UI even fully updates).