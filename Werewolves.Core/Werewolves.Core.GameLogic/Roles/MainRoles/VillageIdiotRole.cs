using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class VillageIdiotRole : RoleHookListener
{
	private static readonly RolePowerDefinition PardonPower = new(
		new RolePowerIdentifier("village-idiot-pardon"),
		RolePowerCategory.Automatic);

	private static readonly Guid PardonResourceId =
		Guid.Parse("4f86b827-47c4-48f8-9ba4-29028d5c75a0");

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal VillageIdiotRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.VillageIdiotRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.VillageIdiot);

	internal bool TryCommitPardon(
		GameSession session,
		IPlayer target,
		out ConfirmationInstruction? consequence)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(target);
		consequence = null;

		if (target.State.Health != PlayerHealth.Alive ||
		    target.State.CurrentRole != MainRoleType.VillageIdiot ||
		    target.State.DurableVotingPower != 1 ||
		    !target.State.HasVotingRight)
		{
			return false;
		}

		var instance = RolePowerInstance.CreateNative(
			target,
			MainRoleType.VillageIdiot,
			PardonPower);
		var identity = new OneUseRolePowerResourceIdentity(
			target.Id,
			MainRoleType.VillageIdiot,
			PardonPower.Identifier.Value,
			instance.Id,
			instance.Origin,
			PardonResourceId);
		if (GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			    session,
			    identity))
		{
			return false;
		}

		var execution = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				target,
				MainRoleType.VillageIdiot,
				PardonPower,
				instance,
				new OneUseRolePowerResource(
					PardonResourceId,
					instance)));
		if (!execution.AvailabilityResult.IsAvailable)
		{
			return false;
		}

		session.CommitGameFact(context =>
			new VillageIdiotPardonCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				PlayerId = target.Id,
				ActingPlayerId = identity.ActingPlayerId,
				SourceRole = identity.SourceRole,
				SourcePowerIdentifier =
					identity.SourcePowerIdentifier,
				PowerInstanceId = identity.PowerInstanceId,
				PowerInstanceOrigin =
					identity.PowerInstanceOrigin,
				OneUseResourceId = identity.OneUseResourceId
			});

		consequence = new ConfirmationInstruction(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
			publicAnnouncement:
				GameStrings.VillageIdiotPardonAnnouncement.Format(
					target.Name));
		return true;
	}

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		HookListenerActionResult.Skip();
}
