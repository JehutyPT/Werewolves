namespace Werewolves.Core.StateModels.Core;

public sealed record DirectionalLivingNeighbors(
	IPlayer? Clockwise,
	IPlayer? Counterclockwise);
