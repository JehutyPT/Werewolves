using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Services;

public abstract record LobbyExitOutcome
{
	private LobbyExitOutcome() { }

	public sealed record InvalidSetup(IReadOnlyList<GameConfigValidationErrorType> Issues) : LobbyExitOutcome;
	public sealed record EvaluationBlocked(LobbyEvaluationState Evaluation) : LobbyExitOutcome;
	public sealed record SetupAcceptanceFailed : LobbyExitOutcome;
	public sealed record ActiveRecoveryWriteFailed : LobbyExitOutcome;
	public sealed record Started(Guid GameId) : LobbyExitOutcome;
	public sealed record ConfigurationRequired(LobbyConfigurationStep Step) : LobbyExitOutcome;
	public sealed record AlreadyActive(Guid GameId) : LobbyExitOutcome;
}

public enum LobbyConfigurationStep
{
	RoleLockIn,
	ActorSetupCards,
	PublicGroupPartition
}
