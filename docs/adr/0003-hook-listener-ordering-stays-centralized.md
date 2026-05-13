# Hook listener dispatch ordering stays centralized

Listener dispatch order within each GameHook is defined in a single, explicit list in GameFlowManager (the `HookListeners` dictionary). Listeners do not declare their own priority or ordering. Adding a new listener means inserting it into the correct position in that list.

Dispatch order is a correctness property of the game rules — the Defender must resolve before Werewolf attacks, Cupid only acts on Night 1 before other roles, the Witch must see the Werewolf victim before deciding. Getting this wrong produces wrong game outcomes, and these bugs are subtle: they only surface in specific role combinations. A single visible list makes ordering trivially auditable against the rulebook.

Distributing priority across listener classes (via attributes, abstract properties, or numeric priorities) would make the ordering invisible at a glance. Inserting a new role in the middle of the sequence would require inspecting every existing listener's priority to determine what to bump. The locality gain (role defines everything about itself) is not worth the correctness risk.

## Considered options

- **Per-listener priority numbers**: each listener declares an integer priority; dispatcher sorts at startup. Rejected because priority numbers are meaningless without seeing all other values, and inserting in the middle forces renumbering across files.
- **Per-listener attributes with ordering**: `[RespondsTo(GameHook.X, Order = N)]` on each class. Same renumbering problem as priority numbers, plus reflection overhead.
- **Listeners declare hooks, ordering stays centralized**: listeners declare *which* hooks they respond to, but the ordering list remains in GameFlowManager. Rejected because it splits registration across two locations without solving the ordering problem, and the factory registration is trivial boilerplate.
