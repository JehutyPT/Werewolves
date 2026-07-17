# Product Vision: Werewolves Companion App

A mobile companion for the human Moderator running a physical game of "The Werewolves of Miller's Hollow."

## Who It Serves

The Moderator — the person standing at the table guiding 8-20 players through the game. Not the players. The players never see or interact with the app.

## What It Does

- Guides the Moderator through game phases (Night, Dawn, Day) with instructions to read aloud and act on privately.
- Tracks game state as the Moderator discovers it — roles revealed, players eliminated, status effects applied.
- Plays atmospheric audio to set the mood during key game moments.
- Gives the Moderator a reference to consult during calm moments: who's alive, which roles remain, what happened this game.
- Protects setup from Already-Decided Role Compositions and Degenerate Simulation Scenarios, where all 1,000 baseline screening runs observe Game Sessions ending during Turn 1.

## What It Does Not Do

- **Make decisions.** The app never suggests who to target, when to use an ability, or whether to tip the scales. That's the Moderator's job.
- **Replace the Moderator.** The physical game happens at the table. The app records outcomes; it doesn't drive them.
- **Require extensive input.** The Moderator is performing — reading announcements, managing energy, keeping tempo. Input must be minimal: a tap, a player selection, a confirmation. Nothing that pulls the Moderator out of the moment.
- **Know everything upfront.** The Moderator deals cards face down and discovers roles as the game unfolds. The app learns alongside them.
- **Present simulated win frequencies as balance guidance.** The current product uses simulation only for pre-game safety screening. Richer, policy-aware simulator guidance is parked as future work in PRD #94, without a near-term delivery commitment.

## Two Tempos, One Device

The Moderator's attention shifts between two modes:

**High-intensity** — phase transitions, role wake-ups, vote outcomes. The Moderator glances at the phone, acts, and returns to the table. The UI must be fast, consistent, and forgiving of imprecise taps in a dark room.

**Low-intensity** — debate, setup. The Moderator has time to browse, review state, and plan. The UI can surface more information and support deliberate interaction.

Every design decision should respect which tempo it serves.
