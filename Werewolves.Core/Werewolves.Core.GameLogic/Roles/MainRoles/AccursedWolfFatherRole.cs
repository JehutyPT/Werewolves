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

internal enum AccursedWolfFatherRoleState
{
	Awake,
	AwaitingInfectionChoice,
	ReadyToSleep,
	Asleep
}

internal sealed class AccursedWolfFatherRole
	: NightRoleHookListener<AccursedWolfFatherRoleState>
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	private static readonly RolePowerDefinition InfectionPower = new(
		new RolePowerIdentifier("accursed-wolf-father-infection"),
		RolePowerCategory.Chosen);

	internal static RolePowerIdentifier InfectionPowerIdentifier =>
		InfectionPower.Identifier;

	internal static readonly Guid InfectionResourceId =
		Guid.Parse("a3d2e55e-0b97-4f4c-a38c-709c03ff1026");

	internal AccursedWolfFatherRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName =>
		GameStrings.AccursedWolfFatherRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.AccursedWolfFather);

	protected override AccursedWolfFatherRoleState WokenUpStateEnum =>
		AccursedWolfFatherRoleState.Awake;

	protected override AccursedWolfFatherRoleState ReadyToSleepStateEnum =>
		AccursedWolfFatherRoleState.ReadyToSleep;

	protected override AccursedWolfFatherRoleState AsleepStateEnum =>
		AccursedWolfFatherRoleState.Asleep;

	protected override bool HasNightPowers => true;

	public override HookListenerActionResult Execute(
		GameSession session,
		ModeratorResponse input)
	{
		if (GetCurrentListenerState(session) == null)
		{
			var retainedVictimId = GetRetainedVictimId(session);
			if (retainedVictimId == null)
			{
				return HookListenerActionResult.Skip();
			}

			var holder = GetAliveRolePlayers(session)?.SingleOrDefault();
			if (holder != null &&
			    IsSpent(session, CreateResourceIdentity(session, holder)))
			{
				return HookListenerActionResult.Skip();
			}
		}

		return base.Execute(session, input);
	}

	public override bool TryResolvePendingInstructionContinuation(
		GameHook hook,
		GameSession session,
		ModeratorInstruction pendingInstruction,
		out string listenerState)
	{
		listenerState = string.Empty;
		if (hook == GameHook.NightMainActionLoop &&
		    pendingInstruction is SelectOptionsInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic
					    .ChooseAccursedWolfFatherInfection
		    } &&
		    HasExpectedAffectedRoleHolders(session, pendingInstruction))
		{
			listenerState =
				AccursedWolfFatherRoleState
					.AwaitingInfectionChoice
					.ToString();
			return true;
		}

		if (hook != GameHook.NightMainActionLoop ||
		    pendingInstruction.Semantic !=
			    ModeratorInstructionSemantic.PutRoleToSleep)
		{
			return base.TryResolvePendingInstructionContinuation(
				hook,
				session,
				pendingInstruction,
				out listenerState);
		}

		var holder = GetAliveRolePlayers(session)?.SingleOrDefault();
		if (holder == null ||
		    pendingInstruction is not ConfirmationInstruction ||
		    pendingInstruction.AffectedPlayerIds is not
			    { Count: 1 } affectedPlayerIds ||
		    affectedPlayerIds.Single() != holder.Id)
		{
			return false;
		}

		var committedInfections =
			GameSessionQueries.GetOrderedNightActionsThisNight(
					session,
					[NightActionType.AccursedWolfFatherInfection])
				.OfType<OneUseRolePowerCommittedLogEntry>()
				.ToArray();
		if (committedInfections.Length == 0)
		{
			return false;
		}

		if (committedInfections is not [var committedInfection])
		{
			throw new InvalidOperationException(
				"The pending Accursed Wolf-Father sleep instruction has multiple infection commits.");
		}

		ValidateOwnedInfectionCommit(session, committedInfection);
		if (committedInfection.ActingPlayerId != holder.Id)
		{
			throw new InvalidOperationException(
				"The pending Accursed Wolf-Father sleep instruction does not belong to the living Role holder.");
		}

		ValidateRetainedVictim(session, committedInfection);
		listenerState = AccursedWolfFatherRoleState.ReadyToSleep.ToString();
		return true;
	}

	internal static bool TryValidateCommittedRecoveryBoundary(
		GameSession session,
		ModeratorInstruction? startingInstruction,
		ModeratorResponse input,
		OneUseRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.ActionType !=
		    NightActionType.AccursedWolfFatherInfection)
		{
			return false;
		}

		ValidateOwnedInfectionCommit(session, committedEntry);
		if (startingInstruction is not SelectOptionsInstruction
		    {
			    Semantic:
				    ModeratorInstructionSemantic
					    .ChooseAccursedWolfFatherInfection,
			    SelectionRange: var selectionRange,
			    Options: var options
		    } ||
		    selectionRange != NumberRangeConstraint.Single ||
		    !options.Select(option => option.Id).SequenceEqual(
			    [
				    AccursedWolfFatherInfectionOptionIds.Infect,
				    AccursedWolfFatherInfectionOptionIds.Decline
			    ],
			    StringComparer.Ordinal) ||
		    input.SelectedOptionIds is not
			    { Count: 1 } selectedOptionIds ||
		    !StringComparer.Ordinal.Equals(
			    selectedOptionIds.Single(),
			    AccursedWolfFatherInfectionOptionIds.Infect))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father commit must correlate to its accepted infection option.");
		}

		ValidateRetainedVictim(session, committedEntry);
		return true;
	}

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

		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.WakeRole,
				GameStrings.RoleWakesUp.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			AccursedWolfFatherRoleState.Awake);
	}

	protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
	[
		CreateStage(
			GameHook.NightMainActionLoop,
			null,
			[
				AccursedWolfFatherRoleState.Awake,
				AccursedWolfFatherRoleState.Asleep
			],
			HandleRoleWakeupAndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			AccursedWolfFatherRoleState.Awake,
			[
				AccursedWolfFatherRoleState.AwaitingInfectionChoice,
				AccursedWolfFatherRoleState.ReadyToSleep
			],
			HandleNightPowerUse_AndId),
		CreateStage(
			GameHook.NightMainActionLoop,
			AccursedWolfFatherRoleState.AwaitingInfectionChoice,
			AccursedWolfFatherRoleState.ReadyToSleep,
			CommitInfectionChoice),
		CreateStage(
			GameHook.NightMainActionLoop,
			AccursedWolfFatherRoleState.ReadyToSleep,
			AccursedWolfFatherRoleState.Asleep,
			HandleAsleepConfirmation),
		CreateEndStage(
			GameHook.NightMainActionLoop,
			AccursedWolfFatherRoleState.Asleep,
			(_, _) => HookListenerActionResult.Complete(
				AccursedWolfFatherRoleState.Asleep))
	];

	protected override HookListenerActionResult HandleNightPowerUse(
		GameSession session,
		ModeratorResponse input)
	{
		var holder = GetHolder(session);
		var victimId = GetRetainedVictimId(session)
			?? throw new InvalidOperationException(
				"The Accursed Wolf-Father infection requires one retained collective victim.");
		var resourceIdentity = CreateResourceIdentity(session, holder);
		if (IsSpent(session, resourceIdentity))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father infection resource is already spent.");
		}

		var instance = CreatePowerInstance(session, holder);
		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				holder,
				MainRoleType.AccursedWolfFather,
				InfectionPower,
				instance,
				new OneUseRolePowerResource(InfectionResourceId, instance)));
		if (!availability.AvailabilityResult.IsAvailable)
		{
			return PrepareSleepInstruction(session);
		}

		return HookListenerActionResult.NeedInput(
			new SelectOptionsInstruction(
				ModeratorInstructionSemantic
					.ChooseAccursedWolfFatherInfection,
				[
					new ModeratorOption(
						AccursedWolfFatherInfectionOptionIds.Infect,
						GameStrings.AccursedWolfFatherInfectOption),
					new ModeratorOption(
						AccursedWolfFatherInfectionOptionIds.Decline,
						GameStrings.DeclineOption)
				],
				NumberRangeConstraint.Single,
				privateInstruction:
					GameStrings.AccursedWolfFatherInfectionInstruction.Format(
						session.GetPlayer(victimId).Name),
				affectedPlayerIds: [holder.Id]),
			AccursedWolfFatherRoleState.AwaitingInfectionChoice);
	}

	private HookListenerActionResult CommitInfectionChoice(
		GameSession session,
		ModeratorResponse input)
	{
		var holder = GetHolder(session);
		var victimId = GetRetainedVictimId(session)
			?? throw new InvalidOperationException(
				"The Accursed Wolf-Father infection requires one retained collective victim.");
		var resourceIdentity = CreateResourceIdentity(session, holder);
		if (IsSpent(session, resourceIdentity))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father infection resource is already spent.");
		}

		var selectedOptionId = input.SelectedOptionIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"The Accursed Wolf-Father infection requires one semantic option.");
		switch (selectedOptionId)
		{
			case AccursedWolfFatherInfectionOptionIds.Infect:
				if (HasInfectionIntentThisNight(session))
				{
					throw new InvalidOperationException(
						"Only one Accursed Wolf-Father infection may be committed per Night.");
				}

				session.CommitOneUseRolePowerNightAction(
					NightActionType.AccursedWolfFatherInfection,
					victimId,
					resourceIdentity);
				break;
			case AccursedWolfFatherInfectionOptionIds.Decline:
				break;
			default:
				throw new InvalidOperationException(
					"The Accursed Wolf-Father infection option is unknown.");
		}

		return PrepareSleepInstruction(session);
	}

	protected override HookListenerActionResult PrepareSleepInstruction(
		GameSession session)
	{
		var holder = GetHolder(session);
		return HookListenerActionResult.NeedInput(
			new ConfirmationInstruction(
				ModeratorInstructionSemantic.PutRoleToSleep,
				GameStrings.RoleGoesToSleepSingle.Format(PublicName),
				affectedPlayerIds: [holder.Id]),
			AccursedWolfFatherRoleState.ReadyToSleep);
	}

	private IPlayer GetHolder(GameSession session) =>
		GetAliveRolePlayers(session)?.SingleOrDefault()
		?? throw new InvalidOperationException(
			"No living Accursed Wolf-Father is available.");

	private static Guid? GetRetainedVictimId(GameSession session)
	{
		return GameSessionQueries.TryGetRetainedWerewolfVictimThisNight(
			session,
			out var victimId)
				? victimId
				: null;
	}

	private static bool HasInfectionIntentThisNight(GameSession session) =>
		GameSessionQueries.GetOrderedNightActionsThisNight(
				session,
				[NightActionType.AccursedWolfFatherInfection])
			.Any();

	private static bool IsSpent(
		GameSession session,
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			resourceIdentity);

	private static RolePowerInstance CreatePowerInstance(
		GameSession session,
		IPlayer holder) =>
		RolePowerInstance.CreateCurrent(
			session,
			holder,
			MainRoleType.AccursedWolfFather,
			InfectionPower);

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		GameSession session,
		IPlayer holder)
	{
		var instance = CreatePowerInstance(session, holder);
		return new OneUseRolePowerResourceIdentity(
			holder.Id,
			MainRoleType.AccursedWolfFather,
			InfectionPowerIdentifier.Value,
			instance.Id,
			instance.Origin,
			InfectionResourceId);
	}

	private static void ValidateOwnedInfectionCommit(
		GameSession session,
		OneUseRolePowerCommittedLogEntry committedEntry)
	{
		var identity = committedEntry.ResourceIdentity;
		if (identity != CreateResourceIdentity(
				session,
				session.GetPlayer(identity.ActingPlayerId)))
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father infection commit has an invalid Role Power identity.");
		}
	}

	private static void ValidateRetainedVictim(
		GameSession session,
		OneUseRolePowerCommittedLogEntry committedEntry)
	{
		if (committedEntry.TargetIds is not [var committedTargetId] ||
		    !GameSessionQueries.TryGetRetainedWerewolfVictimThisNight(
			    session,
			    out var retainedVictimId) ||
		    retainedVictimId != committedTargetId)
		{
			throw new InvalidOperationException(
				"The Accursed Wolf-Father commit must target the one retained collective victim.");
		}
	}
}
