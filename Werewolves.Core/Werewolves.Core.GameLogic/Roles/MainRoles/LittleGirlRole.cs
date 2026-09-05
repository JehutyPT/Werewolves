using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class LittleGirlRole : DeclaredRoleIdentificationOnlyHookListener
{
	internal static readonly RolePowerDefinition SpyingPower = new(
		new RolePowerIdentifier("little-girl-spying"),
		RolePowerCategory.Passive);
	private static readonly ActorBorrowedRolePowerSpec BorrowedPowerSpec = new(
		MainRoleType.LittleGirl,
		SpyingPower);

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
		var borrowedUse = ActorBorrowedRolePowers.ResolveActive(
			session,
			BorrowedPowerSpec);
		var hasBorrowedPower = borrowedUse is not null;
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

		if (borrowedUse is not null)
		{
			attempt = borrowedUse.CreateAttempt();
			return true;
		}

		var actingPlayer = livingHolders.Single();
		var instance = RolePowerInstance.CreateCurrent(
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
}
