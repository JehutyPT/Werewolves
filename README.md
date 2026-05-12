# Werewolves of Miller's Hollow - Moderator Helper

A mobile app that assists a human moderator running a physical game of "The Werewolves of Miller's Hollow." The app tracks game state, guides the moderator through phases, and prompts for input.

## Repository Structure

| Project | Description |
|---------|-------------|
| `Werewolves.Core.StateModels` | State representation, data models, and UI communication contract |
| `Werewolves.Core.GameLogic` | Rules engine, game flow management, and role implementations |
| `Werewolves.Core.Tests` | Integration and unit tests for the core libraries |
| `Werewolves.Client` | .NET MAUI Blazor Hybrid mobile app (Android/iOS) |

## Architecture

- **Core** (`Werewolves.Core/`): Event-sourced game engine with a kernel-facade pattern. Completely UI-agnostic — communicates through `ModeratorInstruction` and `ModeratorResponse` data contracts.
- **Client** (`Werewolves.Client/`): Thin "dumb terminal" that renders state from Core and collects moderator input. Uses MudBlazor with a Model-View-Adapter pattern.

## Scope

**Included:** Base game roles (Villager, Seer, Witch, Werewolf, Hunter, Cupid, Fox, Bear Tamer, Knight, etc.) and 19 specified New Moon event cards.

**Excluded:** Village expansion (Buildings), unspecified New Moon events, advanced variants.
