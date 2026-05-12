Of course. Here is a formal architectural proposal detailing the complete evolution of our discussion, capturing the key decisions, refinements, and their underlying rationale.

---

### **Architectural Proposal: Refactoring the IGameHookListener System**

#### **Abstract**

This proposal outlines a significant architectural refactoring of the `IGameHookListener` system within the `Werewolves.Core` project. The current approach, a flat implementation of the `IGameHookListener` interface, leads to considerable boilerplate and lacks a formal structure for managing complex, stateful role logic. The proposed solution replaces this with a hierarchy of abstract base classes built upon a robust, declarative, data-driven state machine engine. This new architecture will dramatically reduce code duplication, enhance runtime safety through built-in validation, improve developer experience by providing clear patterns for different role complexities, and increase the overall maintainability of the codebase.

#### **1. Motivation**

The existing architecture requires every role and event listener to directly implement the `IGameHookListener` interface. While functional, this has revealed several pain points as more complex roles are considered:

*   **Code Duplication:** Common logic, such as finding the alive players for a given role or managing the basic "wake up -> act -> sleep" lifecycle, is duplicated across multiple listener implementations.
*   **Imperative State Management:** Each listener is responsible for its own state management, typically via imperative `switch` statements or complex `if/else` blocks. This approach is error-prone, difficult to read, and lacks a formal structure, making it easy to introduce bugs by mishandling state transitions.
*   **Lack of Guided Implementation:** There is no clear pattern or "scaffolding" for developers to follow. Implementing a simple role requires the same amount of boilerplate as a complex one, increasing cognitive load and development time.
*   **Implicit Contracts:** The flow of control and the expected state transitions are implicitly defined within the implementation code, making the system difficult to reason about and maintain.

The goal of this refactoring is to address these issues by introducing a structured, reusable, and self-validating framework for all game hook listeners.

#### **2. Proposed Architectural and Documentation Changes**

The proposal centers on replacing the direct implementation of `IGameHookListener` with a system of abstract base classes that provide a declarative state machine engine.

##### **2.1. Key Design Decisions (The Path to the Solution)**

Our discussion explored several paths, leading to key decisions that shaped this final proposal:

1.  **Decision: Keep `ListenerIdentifier` and `IGameHookListener` Separate.**
    *   **Rationale:** An initial idea to merge the `ListenerIdentifier` `struct` into a base `IGameHookListener` class was rejected. The `struct` is a highly efficient, lightweight, value-type key, perfect for dictionary lookups. Converting it to a reference-type base class would introduce unnecessary complexity regarding `Equals()`/`GetHashCode()` implementations and break the clean separation between a listener's identity and its behavior. The current separation is robust and adheres to the Single Responsibility Principle.

2.  **Decision: Reject a Separate `InitialRoleIdentification` Hook.**
    *   **Rationale:** A proposal to handle Night 1 role identification via a dedicated `GameHook` was discarded due to a critical user experience (UX) constraint. For the moderator, a role "waking up" on Night 1 is a single event that seamlessly combines identifying the player(s) and prompting for their action. Introducing a separate hook would create a disjointed UX, making it feel as though roles wake up twice. Therefore, the architecture must support a combined "Identify-then-Act" flow within a single state machine cycle.

##### **2.2. The New Listener Hierarchy**

A new hierarchy of abstract classes will be introduced to provide progressive layers of functionality.

*   **`RoleHookListener` (Non-Generic Base):**
    *   **Purpose:** The universal base for all role listeners.
    *   **Features:** Provides the absolute core logic, most notably a check to ensure at least one player with the role is alive before proceeding. It introduces the `AdvanceCoreStateMachine` abstract method for subclasses to implement.

*   **`RoleHookListener<TRoleStateEnum>` (Generic, Stateful Base):**
    *   **Purpose:** The base for all roles requiring a stateful, multi-step workflow.
    *   **Features:** This class introduces the **declarative state machine engine**. It requires subclasses to implement a `DefineStateMachineStages()` method, which returns a list of `RoleStateMachineStage` objects. The base class acts as the engine, interpreting this list to execute the correct logic based on the current game hook and the listener's state.

##### **2.3. The Declarative State Machine Engine**

The core of the new design is a data-driven state machine.

*   **`RoleStateMachineStage` Record:** A record that declaratively defines a single transition in the state machine. It contains:
    *   The `GameHook` it responds to.
    *   A `StartStage` (the state from which it transitions).
    *   A `Func<>` representing the action logic to execute.
    *   A set of `PossibleEndStages` that the state can validly transition to after the action.
*   **Runtime Safety:** The engine provides crucial runtime validation. Before executing an action, it confirms the current state matches the stage's `StartStage`. After execution, it validates that the new state is one of the `PossibleEndStages`. This prevents illegal state transitions and immediately flags logic errors during development, making the entire system significantly more robust.
*   **Flexibility:** The engine provides helper methods (`CreateStage`, `CreateOpenEndedStage`) that allow for defining both simple, linear flows and complex, branching logic.

##### **2.4. Specialization for Night Roles**

Building on the engine, specialized base classes will handle the common night role lifecycle.

*   **`NightRoleHookListener<T>`:**
    *   **Purpose:** The base for all stateful night roles.
    *   **Features:** Defines the fundamental night lifecycle: `Wake` -> `Act` -> `Sleep`. It provides a default state machine definition that handles waking up, calling an abstract `OnAwakenedRole_AfterId` method for the role's specific action, and then going to sleep. It also contains the now-`virtual` methods (`PrepareWakeupInstructionWithIdRequest`, `ProcessRoleIdentification`) to handle the seamless "Identify-then-Act" flow, allowing non-standard roles (e.g., Thief) to override this behavior as an "escape hatch."

*   **`StandardNightRoleHookListener<T>`:**
    *   **Purpose:** A further specialization for the most common night role pattern: "prompt for a target, then process the selection."
    *   **Features:** Overrides the state machine definition to enforce this stricter workflow. It requires subclasses to implement only two methods: `GenerateTargetSelectionInstruction` and `ProcessTargetSelection`, abstracting away all other state management boilerplate.

*   **`NightRoleIdOnlyHookListener`:**
    *   **Purpose:** For roles that only require identification on Night 1 and have no subsequent night powers (e.g., Two Sisters).
    *   **Features:** Its implementation provides a two-step confirmation flow for the moderator. First, it prompts for identification. Second, it prompts for confirmation that the role is going back to sleep. This was a deliberate design choice to align the application's pace with the real-world friction of managing players at the table, giving the moderator explicit control points.

##### **2.5. Documentation Changes**

The `architecture.md` document will be updated to reflect this new design. The sections describing `IGameHookListener`, `GameFlowManager`, and the overall Game Loop will be modified to detail the new listener hierarchy and the principles of the declarative state machine.

#### **3. Impact Analysis**

##### **Benefits**

*   **Reduced Boilerplate:** The `StandardNightRoleHookListener` and `NightRoleIdOnlyHookListener` will eliminate nearly all boilerplate code for the vast majority of roles.
*   **Increased Robustness & Safety:** The state machine engine's runtime validation of state transitions will prevent a significant class of logic errors, making the system more reliable.
*   **Improved Developer Experience:** Developers are provided with clear, specialized base classes. They can choose the appropriate level of abstraction for the role they are implementing, focusing only on the unique logic.
*   **Enhanced Maintainability & Readability:** A role's entire logical flow is declared in its `DefineStateMachineStages()` method, making it easy to understand and modify its behavior without tracing through complex imperative code.
*   **Guaranteed UX Consistency:** The base classes enforce a consistent user experience for common operations like waking up, identification, and going to sleep.

##### **Considerations & Mitigations**

*   **Consideration: Increased Initial Complexity.** The new architecture is more abstract than a simple interface implementation.
    *   **Mitigation:** The complexity is encapsulated within the base classes. For most roles, developers will interact with the much simpler `StandardNightRoleHookListener`, which hides the underlying engine. Clear documentation and code examples will be provided for all base classes.

*   **Consideration: Migration Effort.** All existing listener implementations will need to be refactored to inherit from the new base classes.
    *   **Mitigation:** This is an upfront investment for substantial long-term gains in maintainability. The structured nature of the new hierarchy will make the migration process for each role a straightforward and predictable task.