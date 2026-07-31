using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Private, canonical commitment of the single unordered Lovers pair.
/// The concrete type and its endpoints are internal so public history
/// consumers cannot recover the relationship.
/// </summary>
internal sealed record LoversPairCommittedLogEntry : GameLogEntryBase
{
	[JsonInclude]
	internal required Guid FirstPlayerId { get; init; }

	[JsonInclude]
	internal required Guid SecondPlayerId { get; init; }

	[JsonInclude]
	internal required Guid ActingPlayerId { get; init; }

	[JsonInclude]
	internal required MainRoleType SourceRole { get; init; }

	[JsonInclude]
	internal required string SourcePowerIdentifier { get; init; }

	[JsonInclude]
	internal required Guid PowerInstanceId { get; init; }

	[JsonInclude]
	internal required RolePowerInstanceOrigin PowerInstanceOrigin { get; init; }

	[JsonInclude]
	internal required FactionFactEffectiveBoundary LinkBoundary { get; init; }

	[JsonIgnore]
	internal IReadOnlyList<Guid> PlayerIds =>
		[FirstPlayerId, SecondPlayerId];

	[JsonIgnore]
	internal RolePowerInstanceIdentity PowerIdentity => new(
		ActingPlayerId,
		SourceRole,
		SourcePowerIdentifier,
		PowerInstanceId,
		PowerInstanceOrigin);

	internal override void EnforceValidity()
	{
		ArgumentNullException.ThrowIfNull(LinkBoundary);
		PowerIdentity.EnforceValidity();
		if (LinkBoundary.TurnNumber != TurnNumber ||
		    LinkBoundary.Phase != CurrentPhase ||
		    FirstPlayerId == Guid.Empty ||
		    SecondPlayerId == Guid.Empty ||
		    FirstPlayerId.CompareTo(SecondPlayerId) >= 0)
		{
			throw new InvalidOperationException(
				"The private Lovers pair commitment is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.SetStatusEffect(
			FirstPlayerId,
			StatusEffectTypes.Lovers,
			isActive: true);
		mutator.SetStatusEffect(
			SecondPlayerId,
			StatusEffectTypes.Lovers,
			isActive: true);
		return this;
	}

	public override string ToString() => "LoversPairCommitted";
}
