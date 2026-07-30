using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Core.GameLogic.Models.EliminationCascades;

internal interface IVoteEliminationInterceptor
{
	bool TryInterceptVoteElimination(
		GameSession session,
		IPlayer target,
		out ConfirmationInstruction? consequence);
}
