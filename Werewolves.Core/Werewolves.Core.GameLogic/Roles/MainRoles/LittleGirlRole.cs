using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class LittleGirlRole : NightRoleIdOnlyHookListener
{
	internal static readonly RolePowerDefinition SpyingPower = new(
		new RolePowerIdentifier("little-girl-spying"),
		RolePowerCategory.Passive);

	internal static bool TryCreateSpyingAttempt(
		GameSession session,
		out RolePowerAttempt attempt)
	{
		ArgumentNullException.ThrowIfNull(session);
		var livingHolders = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.State.CurrentRole == MainRoleType.LittleGirl)
			.ToArray();
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var hasBorrowedPower =
			activation?.SourceRole == MainRoleType.LittleGirl;
		var executionCount =
			livingHolders.Length + (hasBorrowedPower ? 1 : 0);
		if (executionCount == 0)
		{
			attempt = null!;
			return false;
		}

		if (executionCount != 1)
		{
			throw new InvalidOperationException(
				"Little Girl spying requires exactly one active execution.");
		}

		var actingPlayer = hasBorrowedPower
			? session.GetPlayer(activation!.ActingPlayerId)
			: livingHolders.Single();
		var instance = hasBorrowedPower
			? RolePowerInstance.CreateBorrowed(
				session,
				actingPlayer,
				MainRoleType.LittleGirl,
				SpyingPower)
			: RolePowerInstance.CreateCurrent(
				session,
				actingPlayer,
				MainRoleType.LittleGirl,
				SpyingPower);
		attempt = new RolePowerAttempt(
			session,
			actingPlayer,
			MainRoleType.LittleGirl,
			SpyingPower,
			instance);
		return true;
	}

	internal static bool HasValidRetainedGuidanceDecision(
		GameSession session,
		bool continuationRetainsGuidanceDecision,
		bool? retainedGuidanceDecision)
	{
		var hasApplicableExecution =
			TryCreateSpyingAttempt(session, out _);
		return retainedGuidanceDecision.HasValue ==
			(continuationRetainsGuidanceDecision &&
			 hasApplicableExecution);
	}

	internal override string PublicName => GameStrings.LittleGirlRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.LittleGirl);

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input) =>
		session.TurnNumber == 1
			? base.Execute(session, input)
			: HookListenerActionResult.Skip();
}
