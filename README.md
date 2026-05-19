# Werewolves of Miller's Hollow - Moderator Helper

A mobile app that assists a human moderator running a physical game of "The Werewolves of Miller's Hollow." The app tracks game state, guides the moderator through phases, and prompts for input.

## Repository Structure

| Project | Description |
|---------|-------------|
| `Werewolves.Core.StateModels` | State representation, data models, and UI communication contract |
| `Werewolves.Core.GameLogic` | Rules engine, game flow management, and role implementations |
| `Werewolves.Core.Tests` | Integration and unit tests for the core libraries |
| `Werewolves.Client.Shared` | Host-agnostic Razor Class Library for shared Moderator UI, resources, CSS, and client services |
| `Werewolves.Client` | .NET MAUI Blazor Hybrid mobile shell and native service adapters (Android/iOS) |
| `Werewolves.Client.BrowserQaHost` | Local-only browser QA host for inspecting the shared Moderator UI without MAUI |
| `Werewolves.Client.Tests` | Client service, adapter, source-policy, and bUnit rendered component tests |

## Architecture

- **Core** (`Werewolves.Core/`): Event-sourced game engine with a kernel-facade pattern. Completely UI-agnostic — communicates through `ModeratorInstruction` and `ModeratorResponse` data contracts.
- **Shared Moderator UI** (`Werewolves.Client.Shared/`): Host-agnostic Blazor boundary that renders state from Core, collects moderator input, and exposes service contracts for host-only behavior.
- **Mobile Host** (`Werewolves.Client/`): Thin native shell that renders the shared `Routes` component through MAUI Blazor Hybrid and supplies audio, haptics, wake lock, and persistence adapters.
- **Browser QA Host** (`Werewolves.Client.BrowserQaHost/`): Local inspection/debug host that renders the same shared `Routes` component in a normal browser with browser-safe substitutes for native-only behavior.

## Scope

**Included:** Base game roles (Villager, Seer, Witch, Werewolf, Hunter, Cupid, Fox, Bear Tamer, Knight, etc.) and 19 specified New Moon event cards.

**Excluded:** Village expansion (Buildings), unspecified New Moon events, advanced variants.
