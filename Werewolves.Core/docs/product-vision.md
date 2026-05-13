# Product Vision: Werewolves Core Library

A rules engine and state tracker for "The Werewolves of Miller's Hollow." Provides deterministic game logic that any client can drive through a minimal instruction-response protocol.

## What It Does

- Manages the complete game state machine: Night, Dawn, and Day phases with their sub-phases and role interactions.
- Sends Moderator Instructions (what to announce, what to ask) and accepts Moderator Responses (what happened at the table).
- Resolves conflicting night actions deterministically (Witch save vs. Defender protection vs. infection vs. wolf attack).
- Maintains an append-only event log as the single source of truth for all game state.
- Serializes and rehydrates game sessions for persistence across app restarts.

## What It Does Not Do

- **Render UI.** The library has no opinion on how instructions are displayed or how input is collected. It outputs structured data; the client decides how to present it.
- **Make moderator decisions.** The library assumes moderator input is accurate and processes it. It never suggests actions, recommends targets, or evaluates strategy.
- **Tally votes.** Voting happens physically at the table. The library accepts the outcome and tracks consequences.
- **Know roles upfront.** Roles are learned incrementally as the moderator discovers them during gameplay. The library tracks what it's been told, not what it could infer.

## Who Consumes It

Any client that can reference the two .NET assemblies:

- **StateModels** — the public contract. Game session interface, player state, instruction/response types, enums. This is all a client needs to see.
- **GameLogic** — the rules engine. Internal. Clients never reference this directly; they interact through `GameService`.

## Guarantees

- **Determinism.** Given the same event log, the game always reaches the same state.
- **Encapsulation.** Clients cannot mutate game state. The public API is read-only; mutations happen exclusively through the instruction-response cycle.
- **Extensibility.** New roles plug in as hook listeners without modifying the flow manager. The declarative state machine and hook system keep role logic self-contained.
