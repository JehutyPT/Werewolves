### **Architectural Proposal: Refactoring Moderator I/O Contracts**

#### **1. Abstract**

This proposal outlines a plan to refactor the core data contracts between the game logic and the moderator interface. The current `ModeratorInstruction` and `ModeratorInput` classes will be redesigned into a more robust, polymorphic `ModeratorInstruction` hierarchy and a semantically clearer `ModeratorResponse` class. This new architecture enhances the instruction's descriptive capability by separating text intended for public announcement from private moderator guidance. Crucially, it co-locates the creation and contractual validation of a `ModeratorResponse` within its corresponding `ModeratorInstruction` class. A key component is the introduction of a flexible `SelectPlayersInstruction` that uses a range-based constraint system. The primary goal is to significantly improve type safety, expressiveness, encapsulation, and the long-term maintainability of the game's core logic.

#### **2. Motivation**

The existing architecture, while functional, presents several design challenges that will become more pronounced as the game's complexity grows.

*   **Brittleness of Monolithic Classes:** The current `ModeratorInstruction` class acts as a "property bag," forcing consuming code to use switch statements and nullable-type checks, creating a risk of runtime errors.
*   **Lack of Expressiveness:** The single `InstructionText` field fails to distinguish between information that the moderator should read aloud (e.g., "The village goes to sleep") and private guidance for the moderator's eyes only (e.g., "Reminder: Wake the Seer now").
*   **Lack of Encapsulation:** The responsibility for creating a valid `ModeratorInput` is entirely separate from the `ModeratorInstruction` that defines its requirements. This decentralizes validation logic.
*   **Inflexible Player Selection:** The distinction between `PlayerSelectionSingle` and `PlayerSelectionMultiple` is rigid and fails to elegantly handle more nuanced scenarios required by the game rules.
*   **Ambiguous Validation Boundaries:** The current design does not formally distinguish between two critical types of validation: contractual (data format) and game rule (state legality).

This proposal aims to resolve these issues by introducing a more robust, object-oriented design.

#### **3. Proposed Architectural and Documentation Changes**

##### **3.1. `ModeratorInstruction` Class Hierarchy with Dual Text Fields**

The single `ModeratorInstruction` class will be replaced by an abstract base class featuring distinct public and private text fields.

**Abstract Base Class:**
```csharp
public abstract class ModeratorInstruction
{
    /// <summary>
    /// The text to be read aloud or displayed publicly to all players.
    /// </summary>
    public string? PublicAnnouncement { get; protected set; }

    /// <summary>
    /// The text for the moderator's eyes only, containing reminders, rules, or guidance.
    /// </summary>
    public string? PrivateInstruction { get; protected set; }

    public IReadOnlyList<Guid>? AffectedPlayerIds { get; protected set; }

    // A constructor would validate that at least one text field is not empty.
}
```

**Concrete Implementations:**
Each distinct type of instruction will derive from this base. Each class will be responsible for generating its corresponding `ModeratorResponse` via a `CreateResponse` method.

*   **`ConfirmationInstruction`:**
    ```csharp
    public class ConfirmationInstruction : ModeratorInstruction
    {
        // ... constructor to set text ...

        public ModeratorResponse CreateResponse(bool confirmation)
        {
            return new ModeratorResponse { Confirmation = confirmation };
        }
    }
    ```
*   **`SelectPlayersInstruction`:**
    ```csharp
    public class SelectPlayersInstruction : ModeratorInstruction
    {
        public IReadOnlyList<Guid> SelectablePlayerIds { get; }
        public SelectionConstraint Constraint { get; }

        // ... constructor ...

        public ModeratorResponse CreateResponse(IReadOnlyList<Guid> selectedPlayerIds)
        {
            ValidateSelection(selectedPlayerIds); // Contractual validation
            return new ModeratorResponse { SelectedPlayerIds = selectedPlayerIds };
        }

        private void ValidateSelection(IReadOnlyList<Guid> selectedPlayerIds) { /* ... */ }
    }
    ```
*   **`AssignRolesInstruction`:**
    ```csharp
    public class AssignRolesInstruction : ModeratorInstruction
    {
        public IReadOnlyDictionary<Guid, IReadOnlyList<RoleType>> SelectableRolesForPlayers { get; }

        // ... constructor ...

        public ModeratorResponse CreateResponse(Dictionary<Guid, RoleType> assignments)
        {
            ValidateAssignments(assignments); // Contractual validation
            return new ModeratorResponse { AssignedPlayerRoles = assignments };
        }

        private void ValidateAssignments(Dictionary<Guid, RoleType> assignments) { /* ... */ }
    }
    ```

##### **3.2. Generic `SelectionConstraint` for Player Selection**

A new `record struct` will be introduced to define the rules for player selection counts.

```csharp
public readonly record struct SelectionConstraint(int Minimum, int Maximum);
```

This single structure can express all required scenarios:
*   **Exact Selection (N):** `new SelectionConstraint(N, N)`
*   **Vote with Tie (0 or 1):** `new SelectionConstraint(0, 1)`
*   **Witch Potion Use (0 or 1):** `new SelectionConstraint(0, 1)`

##### **3.3. Formalized Two-Tier Validation**

This architecture formally separates validation responsibilities.

1.  **Contractual Validation (Inside `CreateResponse`)**: The `CreateResponse` method within each `ModeratorInstruction` subclass will perform immediate, context-free validation. It will confirm that the provided data adheres to the instruction's contract. A failure here will throw an exception (e.g., `ArgumentException`), indicating a client-side implementation error.

2.  **Game Rule Validation (Inside `GameFlowManager`)**: After a valid `ModeratorResponse` is created, the `GameFlowManager` will perform the second tier of validation. This tier checks if the action is legal within the current game state. A failure here results in a `GameError` object indicating a rule violation.

##### **3.4. Renaming Input Contract to `ModeratorResponse`**

To improve semantic clarity, the existing `ModeratorInput` class will be renamed to `ModeratorResponse`. This change establishes a clear and highly self-documenting request-response pattern: the game engine issues a `ModeratorInstruction` and expects a `ModeratorResponse` in return. This name is more precise than a generic term like `GameInput` and more accurately describes the object's role in the flow of communication.

##### **3.5. Documentation Changes**

The `architecture.md` file will be updated to reflect all the above changes. This includes:
*   Rewriting the section on `ModeratorInstruction` to detail the new polymorphic hierarchy, the public/private text fields, and the `CreateResponse` methods.
*   Replacing all references to `ModeratorInput` with `ModeratorResponse` and updating its section to reflect the new name.
*   Detailing the `SelectionConstraint` structure and the two-tier validation model.

#### **4. Impact Analysis**

##### **Benefits**

*   **Enhanced Type Safety:** Eliminates a class of runtime errors by leveraging the compiler to enforce the relationship between an instruction and its response.
*   **Superior Expressiveness:** The `PublicAnnouncement` and `PrivateInstruction` fields allow the game engine to provide richer, more contextual guidance.
*   **Improved Encapsulation and Cohesion:** The object that defines the requirements for a response is now also responsible for validating and constructing that response.
*   **Increased Flexibility and Maintainability:** The `SelectPlayersInstruction` with `SelectionConstraint` is highly adaptable to new and complex game rules.
*   **Clearer Validation Boundaries:** The formal separation of contractual and game-rule validation makes the system easier to understand, test, and debug.

##### **Considerations & Mitigations**

*   **Consideration: Increased Class Count.**
    *   **Mitigation:** This is a standard and accepted trade-off for the significant benefits of strong typing and clear separation of concerns in an object-oriented system.

*   **Consideration: Client/UI Layer Adaptation.** The client layer must be updated to handle a hierarchy of types and to display both `PublicAnnouncement` and `PrivateInstruction` fields appropriately.
    *   **Mitigation:** This refactoring replaces a `switch` on an enum with a `switch` on an object's type, which is a more robust pattern that maps well to modern UI frameworks.

*   **Consideration: Refactoring Effort.** This is a non-trivial change that will touch several parts of the communication layer.
    *   **Mitigation:** The changes are well-contained within the I/O boundary of the core game engine. The refactoring can be executed methodically: first, define the new instruction and response classes; second, update the `GameFlowManager` to produce the new instructions; finally, update the consuming client layer to handle them.