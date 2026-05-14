# Win probability simulator reuses the engine via a headless driver

The Win Probability Simulator drives the existing `Werewolves.Core` engine through a headless driver — a layer that reads `ModeratorInstruction`s and produces automated `ModeratorResponse`s via a pluggable decision strategy — rather than maintaining a separate analytical or closed-form model of game outcomes. Each Monte Carlo run instantiates a real `GameSession` and plays it to completion.

The engine is the source of truth for game mechanics: hook ordering, faction-membership changes (infection, Wild Child transformation), elimination chains (Lovers heartbreak, Hunter shot, Rusty Sword), and win-condition resolution. A separate analytical model would have to re-encode all of that, and the role catalog grows over time. Engine reuse guarantees that new roles participate in simulations automatically and that the simulator can never drift away from the engine's rule resolution.

The spike (#30) validated that this approach meets the performance budget on mobile: 1,000 complete games in 2.5–3.0 seconds on a Samsung S7, well under the 15-second go/no-go threshold. The headless driver pattern from the spike (`HeadlessGameDriver` + `IModeratorDecisionStrategy`) becomes the foundation — swapping `FirstValidOptionStrategy` for a random or stratified strategy is a strategy implementation, not a driver change.

## Considered options

- **Separate analytical / closed-form model**: derive win probabilities from a hand-built model of role interactions. Rejected because the engine already encodes every rule the simulator needs (hook ordering, elimination chains, infection mechanics, win conditions), and the catalog is large and growing. Every new role would require duplicate work in the analytical model, and any divergence between the two would silently produce wrong probabilities.
- **Hybrid engine + analytical shortcuts**: use the engine for most roles, but short-circuit "easy" cases (e.g., pure Werewolves vs. pure Villagers) analytically for speed. Rejected because the spike showed engine throughput is comfortably under budget — there is no performance pressure justifying the maintenance burden of a hybrid boundary, and that boundary would itself need to be re-evaluated each time a role is added.
