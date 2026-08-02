using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class PrejudicedManipulatorRole : NightRoleIdOnlyHookListener
{
	internal override string PublicName =>
		GameStrings.PrejudicedManipulatorRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.PrejudicedManipulator);

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input) =>
		session.TurnNumber == 1
			? base.Execute(session, input)
			: HookListenerActionResult.Skip();

	protected override void ProcessRoleIdentification(
		GameSession session,
		ModeratorResponse input)
	{
		var holderId = input.SelectedPlayerIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"Prejudiced Manipulator identification requires exactly one Player.");
		if (holderId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"Prejudiced Manipulator identification requires exactly one Player.");
		}

		if (session.GetPlayerState(holderId).PhysicalCharacterCardId is null)
		{
			var card = session.GetModeratorPhysicalCharacterCards()
				.FirstOrDefault(state =>
					state.Zone == PhysicalCharacterCardZone.DealPool &&
					state.OwnerPlayerId is null &&
					state.Card.PrintedRole ==
						MainRoleType.PrejudicedManipulator)
				?.Card ?? throw new InvalidOperationException(
					"No unowned Prejudiced Manipulator Physical Character Card is available.");
			if (!session.TryRecordPhysicalCharacterCardOwnership(
				session.RoleLockIn.Version,
				holderId,
				card.Id))
			{
				throw new InvalidOperationException(
					"The identified Prejudiced Manipulator Physical Character Card could not be bound.");
			}
		}

		base.ProcessRoleIdentification(session, input);
		InitialBeneficiaryClosureRules.TryCommitCurrentSession(session);
	}
}
