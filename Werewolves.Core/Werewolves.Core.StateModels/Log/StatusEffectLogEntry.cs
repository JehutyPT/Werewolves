using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

public record StatusEffectLogEntry : GameLogEntryBase
{
	public required StatusEffectTypes EffectType { get; init; }
	public required Guid PlayerId { get; init; }
	
	/// <summary>
	/// Applies the status effect to the game state.
	/// </summary>
	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.SetStatusEffect(PlayerId, EffectType, true);

		return this;
	}

	public override string ToString() =>
		$"StatusEffect: {EffectType} on {PlayerId}";
}