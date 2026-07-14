using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

public sealed class ActorSetupCards
{
	public const int RequiredCount = 3;

	public static ActorSetupCards None { get; } = new([]);

	public IReadOnlyList<MainRoleType> Cards { get; }

	public ActorSetupCards(IEnumerable<MainRoleType> cards)
	{
		ArgumentNullException.ThrowIfNull(cards);
		Cards = Array.AsReadOnly(cards.ToArray());
	}
}
