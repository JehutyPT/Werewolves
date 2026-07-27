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

internal enum WitchRoleState
{
	Awake,
	AwaitingHealingSelection,
	AwaitingPoisonSelection,
	ReadyToSleep,
	Asleep
}

internal sealed class WitchRole : NightRoleHookListener<WitchRoleState>
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private bool _attackRosterDisclosed;

	private static readonly RolePowerDefinition PotionsPower = new(
		new RolePowerIdentifier("witch-potions"),
		RolePowerCategory.Chosen);

	internal static readonly Guid HealingResourceId =
		Guid.Parse("a9b9d885-3edc-4671-bec8-1ddabbe4de3e");

	internal static readonly Guid PoisonResourceId =
		Guid.Parse("da29bd31-bbe8-4abc-bb12-87b15df6df38");

	internal WitchRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.WitchRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Witch);

	protected override WitchRoleState WokenUpStateEnum => WitchRoleState.Awake;

	protected override WitchRoleState ReadyToSleepStateEnum => WitchRoleState.ReadyToSleep;

	protected override WitchRoleState AsleepStateEnum => WitchRoleState.Asleep;

	protected override bool HasNightPowers => true;

	protected override HookListenerActionResult HandleRoleWakeupAndId(
		GameSession session,
		ModeratorResponse input)
	{
		var result = base.HandleRoleWakeupAndId(session, input);
		if (result.Instruction is not ConfirmationInstruction
		    {
			    Semantic: ModeratorInstructionSemantic.WakeRole
		    })
		{
			return result;
		}

		var witch = GetWitch(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [witch.Id]),
			WitchRoleState.Awake);
	}

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (GetCurrentListenerState(session) == null)
		{
			_attackRosterDisclosed = false;
			var witch = GetAliveRolePlayers(session)?.SingleOrDefault();
			if (witch != null)
			{
				var instance = CreatePowerInstance(witch);
				if (IsSpent(
					    session,
					    CreateResourceIdentity(
						    witch,
						    instance,
						    HealingResourceId)) &&
				    IsSpent(
					    session,
					    CreateResourceIdentity(
						    witch,
						    instance,
						    PoisonResourceId)))
				{
					return HookListenerActionResult.Skip();
				}
			}
		}

		return base.Execute(session, input);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[WitchRoleState.Awake, WitchRoleState.Asleep],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.Awake,
			[
				WitchRoleState.AwaitingHealingSelection,
				WitchRoleState.AwaitingPoisonSelection,
				WitchRoleState.ReadyToSleep
			],
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.AwaitingHealingSelection,
			[
				WitchRoleState.AwaitingPoisonSelection,
				WitchRoleState.ReadyToSleep
			],
			HandleHealingSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.AwaitingPoisonSelection,
			WitchRoleState.ReadyToSleep,
			HandlePoisonSelection),
		CreateStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.ReadyToSleep,
			WitchRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			WitchRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(WitchRoleState.Asleep))
	];

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var witch = GetWitch(session);
		var attackTargets = GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var instance = CreatePowerInstance(witch);
		if (attackTargets.Count > 0 &&
		    TryEvaluateAvailableResource(
			    session,
			    witch,
			    instance,
			    HealingResourceId))
		{
			_attackRosterDisclosed = true;
			return HookListenerActionResult.NeedInput(
				CreateHealingInstruction(witch, attackTargets),
				WitchRoleState.AwaitingHealingSelection);
		}

		return PreparePoisonOrSleep(session, witch, healedTargetId: null);
	}

	private HookListenerActionResult HandleHealingSelection(
		GameSession session,
		ModeratorResponse input)
	{
		var selectedPlayerIds = input.SelectedPlayerIds
			?? throw new InvalidOperationException(
				"Witch healing requires a Player selection response.");
		var targetId = selectedPlayerIds.SingleOrDefault();
		var nextInstruction = PreparePoisonOrSleep(
			session,
			GetWitch(session),
			selectedPlayerIds.Count == 0 ? null : targetId);
		if (selectedPlayerIds.Count > 0)
		{
			CommitPotion(
				session,
				GetWitch(session),
				HealingResourceId,
				NightActionType.WitchSave,
				targetId);
		}

		return nextInstruction;
	}

	private HookListenerActionResult HandlePoisonSelection(
		GameSession session,
		ModeratorResponse input)
	{
		var selectedPlayerIds = input.SelectedPlayerIds
			?? throw new InvalidOperationException(
				"Witch poison requires a Player selection response.");
		var nextInstruction = PrepareSleepInstruction(session);
		if (selectedPlayerIds.Count > 0)
		{
			CommitPotion(
				session,
				GetWitch(session),
				PoisonResourceId,
				NightActionType.WitchKill,
				selectedPlayerIds.Single());
		}

		return nextInstruction;
	}

	private HookListenerActionResult PreparePoisonOrSleep(
		GameSession session,
		IPlayer witch,
		Guid? healedTargetId)
	{
		var poisonCandidates = session.GetPlayers()
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.Id != witch.Id &&
				player.Id != healedTargetId)
			.Select(player => player.Id)
			.ToHashSet();

		var instance = CreatePowerInstance(witch);
		if (poisonCandidates.Count == 0 ||
		    !TryEvaluateAvailableResource(
			    session,
			    witch,
			    instance,
			    PoisonResourceId))
		{
			return PrepareSleepInstruction(session);
		}

		var attackTargets = GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var attackRosterWasDisclosed =
			_attackRosterDisclosed ||
			session.PendingModeratorInstruction?.Semantic ==
				ModeratorInstructionSemantic.SelectWitchHealingTarget;
		var privateInstruction = attackRosterWasDisclosed || attackTargets.Count == 0
			? GameStrings.WitchPoisonSelectionInstruction
			: GameStrings.WitchAttackTargetsAndPoisonSelectionInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name)));
		_attackRosterDisclosed = true;

		return HookListenerActionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
				poisonCandidates,
				NumberRangeConstraint.SingleOptional,
				privateInstruction: privateInstruction,
				affectedPlayerIds: [witch.Id])
			{
				EmptySelectionOptionLabel = GameStrings.DeclineOption
			},
			WitchRoleState.AwaitingPoisonSelection);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var witch = GetWitch(session);
		var attackTargets = GameSessionQueries.GetPhysicalAttackTargetsThisNight(session);
		var instance = CreatePowerInstance(witch);
		var healingIdentity = CreateResourceIdentity(
			witch,
			instance,
			HealingResourceId);
		var poisonIdentity = CreateResourceIdentity(
			witch,
			instance,
			PoisonResourceId);
		var wasAttackRosterDisclosed =
			_attackRosterDisclosed ||
			session.PendingModeratorInstruction?.Semantic is
				ModeratorInstructionSemantic.SelectWitchHealingTarget or
				ModeratorInstructionSemantic.SelectWitchPoisonTarget ||
			session.GameHistoryLog
				.OfType<OneUseRolePowerCommittedLogEntry>()
				.Any(entry =>
					entry.TurnNumber == session.TurnNumber &&
					entry.CurrentPhase == GamePhase.Night &&
					(entry.ResourceIdentity == healingIdentity ||
					 entry.ResourceIdentity == poisonIdentity));
		var privateInstruction = !wasAttackRosterDisclosed && attackTargets.Count > 0
			? GameStrings.WitchAttackTargetsInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name)))
			: null;
		_attackRosterDisclosed = true;

		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				privateInstruction,
				[witch.Id]),
			WitchRoleState.ReadyToSleep);
	}

	private bool TryEvaluateAvailableResource(
		GameSession session,
		IPlayer witch,
		RolePowerInstance instance,
		Guid resourceId)
	{
		var resourceIdentity = CreateResourceIdentity(
			witch,
			instance,
			resourceId);
		if (IsSpent(session, resourceIdentity))
		{
			return false;
		}

		var resource = new OneUseRolePowerResource(resourceId, instance);
		return _availabilityGateway.Evaluate(
				new RolePowerAttempt(
					witch,
					MainRoleType.Witch,
					PotionsPower,
					instance,
					resource))
			.AvailabilityResult.IsAvailable;
	}

	private static bool IsSpent(
		GameSession session,
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity);

	private static RolePowerInstance CreatePowerInstance(IPlayer witch) =>
		RolePowerInstance.CreateNative(
			witch,
			MainRoleType.Witch,
			PotionsPower);

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		IPlayer witch,
		RolePowerInstance instance,
		Guid resourceId) => new(
			witch.Id,
			MainRoleType.Witch,
			PotionsPower.Identifier.Value,
			instance.Id,
			instance.Origin,
			resourceId);

	private static void CommitPotion(
		GameSession session,
		IPlayer witch,
		Guid resourceId,
		NightActionType actionType,
		Guid targetId)
	{
		var instance = CreatePowerInstance(witch);
		var resourceIdentity = CreateResourceIdentity(
			witch,
			instance,
			resourceId);
		if (IsSpent(session, resourceIdentity))
		{
			throw new InvalidOperationException(
				"The selected Witch potion resource is already spent.");
		}

		session.CommitOneUseRolePowerNightAction(
			actionType,
			targetId,
			resourceIdentity);
	}

	private static SelectPlayersInstruction CreateHealingInstruction(
		IPlayer witch,
		IReadOnlyList<IPlayer> attackTargets) =>
		new(
			ModeratorInstructionSemantic.SelectWitchHealingTarget,
			attackTargets.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.WitchHealingSelectionInstruction.Format(
				string.Join(", ", attackTargets.Select(player => player.Name))),
			affectedPlayerIds: [witch.Id])
		{
			EmptySelectionOptionLabel = GameStrings.DeclineOption
		};

	private IPlayer GetWitch(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No alive Witch found for potion selection.");
}
