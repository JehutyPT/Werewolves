using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

public sealed record AngelExpiredLogEntry : GameLogEntryBase
{
    internal override void EnforceValidity()
    {
        if (TurnNumber != 2 || CurrentPhase != GamePhase.Day)
        {
            throw new InvalidOperationException(
                "Angel expiry must be committed immediately after the Dawn victory window resolving Night 2.");
        }
    }

    protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

    public override string ToString() => "AngelExpired";
}
