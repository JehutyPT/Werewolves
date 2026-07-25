# Stable recovery snapshots discard live execution state

Serialization stores the latest stable Main Phase recovery snapshot, not an event-log-only payload and not the live in-memory execution tail. A stable boundary exists only after `GameFlowManager` has routed phase work, applied any victory override, and committed the next `PendingInstruction`.

The durable snapshot contains the Game Session identity and setup (`Id`, `SeatingOrder`, `RolesInPlay`), derived state as of the boundary (`Players`, `TurnNumber`), the boundary `GameHistoryLog`, the committed boundary `PendingInstruction`, and a minimal `PhaseStateCache` cursor. `PendingInstruction` is durable because it is the stable instruction the moderator must consume next after Rehydration; it is not arbitrary in-flight listener state.

`PhaseStateCache` remains in the DTO for compatibility with the existing serialized shape, but ADR-0002 constrains how it is read. Stable-boundary Rehydration restores only `CurrentPhase`, `SubPhase`, and `CompletedSubPhaseStages`, which are the cursor needed to consume the committed boundary instruction. Active sub-phase stage, active listener id/type, and listener state are transient execution details and are ignored.

This means if the OS kills the app mid-night, mid-dawn, or mid-day tail work, the moderator resumes from the latest stable boundary and must redo work that had not become durable. This is an accepted trade-off: backgrounding preserves process state; only a full process kill triggers recovery. Serializing exact listener progress would leak state-machine internals into the persistence format and create a brittle versioning surface.

The client may attempt to write a save file after each successful `ProcessInput()`, but the serialized payload only advances when the Core captures a new stable boundary. Save-file writes use a temporary file in the save directory followed by replace/rename into place. If atomic replacement is unavailable on a platform, same-directory rename overwrite is the accepted fallback; either way the destination file is not directly truncated before the new payload is written.

## Considered options

- **Event-log-only serialization**: keeps the payload small and theoretically canonical, but current Rehydration intentionally restores cached state directly and does not replay log entries. Rejected unless the implementation is redesigned around replay.
- **Serialize transient state**: would allow exact resume from any point, but active stage/listener state is deeply coupled to declarative state-machine internals. Rejected for v1.
- **Stable recovery snapshot**: stores enough derived state and cursor data to resume from committed Main Phase boundaries while discarding live execution state. Accepted.

## Amendment: ADR-0017

ADR-0017 adds one narrow target exception for a successful Thief `Offer1`, `Offer2`, or `Decline` response. Before Core returns success, that response must atomically create a stable checkpoint containing the committed outcome, resulting card zones, current Role and fresh power state, and the pending public sleep instruction. This does not make arbitrary listener progress durable: it promotes only the complete Thief outcome and its next semantic instruction so recovery cannot repeat an already performed physical exchange or reopen a committed decline. The current implementation remains on Main-Phase-only boundaries until the Thief contract lands.

## Amendment: accepted observation recovery boundaries

Once accepted, Role Identification, Faction Agent Group Observation, or Role Reveal becomes durable together with the Moderator instruction that follows it. Rehydration resumes at that instruction without asking for or applying the accepted observation again. This preserves the complete observation boundary without making partial work in progress durable.
