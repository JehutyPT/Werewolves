using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Client.Fixtures;

/// <summary>Prepared Game Sessions for deterministic tests and Browser QA, without Lobby evaluation.</summary>
public static class GameSessionFixture
{
	public static StartGameConfirmationInstruction StartPreparedGame(
		this GameClientManager manager,
		IReadOnlyList<string> playerNamesInOrder,
		IReadOnlyList<MainRoleType> rolesInPlay) =>
		manager.StartPreparedGame(new GameSessionConfig(playerNamesInOrder.ToList(), rolesInPlay.ToList()));

	public static StartGameConfirmationInstruction StartPreparedGame(
		this GameClientManager manager,
		GameSessionConfig config) => manager.StartFixtureGame(config);
}
