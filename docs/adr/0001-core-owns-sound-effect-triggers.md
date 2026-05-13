# Core library owns sound effect triggers

The core library determines *which* sound effects to play and *when*, via the `SoundEffects` list on `ModeratorInstruction`. The client resolves identifiers to actual audio files and handles playback mechanics (looping, volume, muting).

Sound effect triggers are a game-flow concern, not a presentation concern. The decision to play atmospheric audio when werewolves wake up is correct regardless of the client platform. Placing this logic in the client would mean duplicating it for every client implementation (MAUI, a future web client via REST-wrapped core, etc.) with no benefit. The core uses semantic identifiers (not filenames), leaving the client free to resolve them to different audio packs for a customizable moderator experience.

## Considered options

- **Client owns audio decisions**: the client infers what to play from instruction type and context. Rejected because the mapping logic would be duplicated across clients, and the core already has the richest context about what game moment is occurring.
- **Core specifies filenames**: too coupled to a specific asset set. Semantic identifiers preserve the boundary.
