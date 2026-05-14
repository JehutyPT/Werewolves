using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Interfaces;

public interface IModeratorDecisionStrategy
{
	ModeratorResponse CreateResponse(ModeratorInstruction instruction, IGameSession session);
}
