using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records a resolved Dawn consequence before the affected Player publicly
/// reveals their Role and the elimination is applied.
/// </summary>
public sealed record DawnVictimDeterminedLogEntry : GameLogEntryBase
{
    public required Guid PlayerId { get; init; }

    public required EliminationReason Reason { get; init; }

    protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

    public override string ToString() =>
        $"DawnVictimDetermined: {PlayerId} ({Reason})";
}
