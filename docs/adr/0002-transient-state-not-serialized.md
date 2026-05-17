# Transient execution state is not serialized

Only the event log (persistent state) is serialized. Transient execution state — current phase position, pending instruction, listener state machines — is not persisted and resets to the beginning of the current main phase on rehydration.

This means if the OS kills the app mid-night-phase, the moderator must redo that phase's instructions from the start. This is an accepted trade-off: the scenario is rare (backgrounding preserves process state; only a full process kill triggers it), and when it does happen the moderator can re-enter the same answers or the group benefits from replaying the phase after a long interruption. Serializing transient state would add significant complexity to the core's serialization model for a marginal edge case.

Persistence writes the latest stable main-phase recovery boundary, not the live in-memory execution tail. Current-phase log entries, player-state consequences, sub-phase progress, and listener progress are volatile until `GameFlowManager` has fully settled a main-phase boundary and updated the next pending instruction. Rehydration may restore the minimal stable cursor needed to consume that committed boundary instruction, but it does not make arbitrary mid-phase work durable.

## Considered options

- **Serialize transient state**: would allow perfect resume from any point, but the transient state (phase position, listener state machines, pending instruction) is deeply coupled to the declarative state machine internals. Serializing it would leak execution details into the persistence format and create a brittle versioning surface. Rejected for v1.
