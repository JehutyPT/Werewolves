using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

public record StatusEffectLogEntry : GameLogEntryBase
{
	public required StatusEffectTypes EffectType { get; init; }
	public required Guid PlayerId { get; init; }
	
	/// <summary>
	/// Applies the status effect to the game state.
	/// 
	/// Note: While most status effects are handled uniformly via SetStatusEffect(),
	/// WildChildChanged has special behavior that also changes the player's role.
	/// This is an intentional divergence from the pure unified pattern to preserve
	/// the gameplay logic where Wild Child transforms into a SimpleWerewolf.
	/// </summary>
	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		// Special case: WildChildChanged also changes the player's role
		if (EffectType == StatusEffectTypes.WildChildChanged)
		{
			mutator.SetPlayerRole(PlayerId, MainRoleType.SimpleWerewolf);
		}
		
		// Apply the status effect flag uniformly for all effect types
		mutator.SetStatusEffect(PlayerId, EffectType, true);

		return this;
	}

	public override string ToString() =>
		$"StatusEffect: {EffectType} on {PlayerId}";
}