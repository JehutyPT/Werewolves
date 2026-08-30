using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit.Abstractions;
using static Werewolves.Core.StateModels.Enums.GameHook;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Holds the inputs needed for night phase actions.
/// Each role that acts at night has optional properties here.
/// </summary>
public class NightActionInputs
{
    /// <summary>
    /// Werewolf action: IDs of the Werewolf Faction Agents to observe when
    /// the complete living group is not yet known.
    /// </summary>
    public HashSet<Guid>? WerewolfIds { get; init; }

    /// <summary>
    /// Werewolf action: ID of the victim to select.
    /// </summary>
    public Guid? WerewolfVictimId { get; init; }

    /// <summary>
    /// Seer action: ID of the Seer player to identify.
    /// </summary>
    public Guid? SeerId { get; init; }

    /// <summary>
    /// Seer action: ID of the player for the Seer to investigate.
    /// </summary>
    public Guid? SeerTargetId { get; init; }

    /// <summary>
    /// Accursed Wolf-Father action: ID of the role holder to identify.
    /// </summary>
    public Guid? AccursedWolfFatherId { get; init; }

    /// <summary>
    /// Accursed Wolf-Father action: whether to infect the retained collective victim.
    /// </summary>
    public bool? AccursedWolfFatherInfectsVictim { get; init; }

    /// <summary>
    /// Big Bad Wolf action: ID of the role holder to identify.
    /// </summary>
    public Guid? BigBadWolfId { get; init; }

    /// <summary>
    /// Big Bad Wolf action: ID of the additional victim to select.
    /// </summary>
    public Guid? BigBadWolfTargetId { get; init; }

    /// <summary>
    /// Actor action: ID of the Actor player to identify or wake.
    /// </summary>
    public Guid? ActorId { get; init; }

    /// <summary>
    /// Actor action: ID of the setup card to borrow, or null to decline.
    /// ActorId distinguishes a declined choice from an omitted Actor action.
    /// </summary>
    public Guid? ActorSetupCardId { get; init; }

    // Future roles can add their inputs here, e.g.:
    // public Guid? WitchHealTargetId { get; init; }
    // public Guid? WitchPoisonTargetId { get; init; }
    // public Guid? DefenderProtectTargetId { get; init; }
}

/// <summary>
/// Fluent builder for creating test game scenarios with minimal boilerplate.
/// </summary>
public class GameTestBuilder
{
    private List<string> _playerNames = [];
    private List<MainRoleType> _roles = [];
	private ActorSetupCards? _actorSetupCards;
    private GameService _gameService = new();
    private Guid _gameId;
    private bool _gameStarted;
    private ModeratorInstruction? _lastInstruction = null;
    private readonly DiagnosticStateObserver _diagnosticObserver = new();
    private readonly ITestOutputHelper? _output;

    private GameTestBuilder(ITestOutputHelper? output = null)
    {
        _output = output;
    }

	/// <summary>
	/// Creates a new test builder instance.
	/// </summary>
	public static GameTestBuilder Create(ITestOutputHelper? output = null) => new(output);

	internal static GameTestBuilder ForExistingGame(
		GameService gameService,
		Guid gameId) =>
		new()
		{
			_gameService = gameService ??
				throw new ArgumentNullException(nameof(gameService)),
			_gameId = gameId,
			_gameStarted = true
		};

	internal GameTestBuilder WithRolePowerAvailabilityPolicy(
		IRolePowerAvailabilityPolicy policy)
	{
		if (_gameStarted)
		{
			throw new InvalidOperationException(
				"The Role Power availability policy must be configured before starting the game.");
		}

		_gameService = new GameService(policy);
		return this;
	}

	internal GameTestBuilder WithOptionalRolePowerAvailabilityPolicy(
		IRolePowerAvailabilityPolicy? policy) =>
		policy == null ? this : WithRolePowerAvailabilityPolicy(policy);

	internal GameTestBuilder WithEliminationCascadeReaction(
		IEliminationCascadeReaction reaction,
		EliminationCascadeReactionBoundary boundary =
			EliminationCascadeReactionBoundary.Forced)
		=> WithEliminationCascadeReactions(
			new EliminationCascadeReactionBinding(
				reaction,
				boundary));

	internal GameTestBuilder WithEliminationCascadeReactions(
		params EliminationCascadeReactionBinding[] reactions)
	{
		if (_gameStarted)
		{
			throw new InvalidOperationException(
				"The Elimination Cascade reaction must be configured before starting the game.");
		}

		_gameService = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			reactions);
		return this;
	}

    /// <summary>
    /// Adds players with auto-generated names (Player1, Player2, etc.).
    /// </summary>
    public GameTestBuilder WithPlayers(int count)
    {
        _playerNames = Enumerable.Range(1, count)
            .Select(i => $"Player{i}")
            .ToList();
        return this;
    }

    /// <summary>
    /// Adds players with specific names in seating order.
    /// </summary>
    public GameTestBuilder WithPlayers(params string[] names)
    {
        _playerNames = [.. names];
        return this;
    }

    /// <summary>
    /// Sets the roles for the game. Count must match player count.
    /// </summary>
    public GameTestBuilder WithRoles(params MainRoleType[] roles)
    {
        _roles = [.. roles];
        return this;
    }

	internal GameTestBuilder WithActorSetupCards(ActorSetupCards setupCards)
	{
		if (_gameStarted)
		{
			throw new InvalidOperationException(
				"Actor Setup Cards must be configured before starting the game.");
		}

		_actorSetupCards = setupCards ??
			throw new ArgumentNullException(nameof(setupCards));
		return this;
	}

    /// <summary>
    /// Creates a simple game with werewolves, seer, and villagers.
    /// </summary>
    /// <param name="playerCount">Total players (minimum 4)</param>
    /// <param name="werewolfCount">Number of werewolves (default 1)</param>
    /// <param name="includeSeer">Include a seer (default true)</param>
    public GameTestBuilder WithSimpleGame(int playerCount, int werewolfCount = 1, bool includeSeer = true)
    {
        if (playerCount < 3)
            throw new ArgumentException(CoreTestReferences.ExceptionMessages.MinimumPlayersRequired(3), nameof(playerCount));

        _playerNames = Enumerable.Range(1, playerCount)
            .Select(i => $"Player{i}")
            .ToList();

        _roles = [];
        
        // Add werewolves
        for (int i = 0; i < werewolfCount; i++)
            _roles.Add(MainRoleType.SimpleWerewolf);
        
        // Add seer if requested
        if (includeSeer)
            _roles.Add(MainRoleType.Seer);
        
        // Fill remaining with villagers
        int villagersNeeded = playerCount - _roles.Count;
        for (int i = 0; i < villagersNeeded; i++)
            _roles.Add(MainRoleType.SimpleVillager);

        return this;
    }

    /// <summary>
    /// Starts the game and returns the confirmation instruction.
    /// </summary>
    public StartGameConfirmationInstruction StartGame()
    {
        if (_playerNames.Count != _roles.Count)
            throw new InvalidOperationException(
                CoreTestReferences.ExceptionMessages.PlayerCountMustMatchRoleCount(_playerNames.Count, _roles.Count));

		var instruction = _actorSetupCards == null
			? _gameService.StartNewGameWithObserver(
				_playerNames,
				_roles,
				stateChangeObserver: _diagnosticObserver)
			: _gameService.StartNewGame(
				new GameSessionConfig(
					_playerNames,
					_roles,
					_actorSetupCards));
        _lastInstruction = instruction;
		_gameId = instruction.GameGuid;
        _gameStarted = true;
        
        // Wire up session for GUID-to-name resolution in diagnostics
        var session = _gameService.GetGameStateView(_gameId);
        if (session != null)
            _diagnosticObserver.SetSession(session);
        
        return instruction;
    }

	internal GameTestBuilder ArrangePartiallyKnownThreeBrothers(Guid committedBrotherId)
	{
		EnsureGameStarted();
		var session = GetMutableSessionForArrangement();
		session.AssignRole(committedBrotherId, MainRoleType.ThreeBrothers);
		session.IdentifyRole([committedBrotherId], MainRoleType.ThreeBrothers);
		return this;
	}

	internal GameTestBuilder ArrangeKnownThreeBrothers(
		IReadOnlySet<Guid> brotherIds)
	{
		EnsureGameStarted();
		var session = GetMutableSessionForArrangement();
		var committedBrotherIds = brotherIds.ToHashSet();
		session.AssignRole(committedBrotherIds, MainRoleType.ThreeBrothers);
		session.IdentifyRole(committedBrotherIds, MainRoleType.ThreeBrothers);
		return this;
	}

	internal GameTestBuilder ArrangeThreeBrotherLeavesCurrentRole(Guid brotherId)
	{
		EnsureGameStarted();
		GetMutableSessionForArrangement().AssignRole(
			brotherId,
			MainRoleType.SimpleVillager);
		return this;
	}

	internal GameTestBuilder ArrangeKnownRole(
		Guid playerId,
		MainRoleType role)
	{
		EnsureGameStarted();
		var session = GetMutableSessionForArrangement();
		session.AssignRole(playerId, role);
		session.IdentifyRole([playerId], role);
		return this;
	}

	internal GameTestBuilder ArrangeKnownPhysicalRole(
		Guid playerId,
		MainRoleType role)
	{
		EnsureGameStarted();
		var session = GetMutableSessionForArrangement();
			var card = session.GetModeratorPhysicalCharacterCards()
				.First(state =>
				(state.Zone == PhysicalCharacterCardZone.DealPool ||
				 state is
				 {
					 Zone: PhysicalCharacterCardZone.PlayerOwned,
					 OwnerPlayerId: var ownerId
				 } && ownerId == playerId) &&
				state.Card.PrintedRole == role);
		if (card.Zone == PhysicalCharacterCardZone.DealPool &&
			!session.TryRecordPhysicalCharacterCardOwnership(
				session.RoleLockIn.Version,
				playerId,
				card.Card.Id))
		{
			throw new InvalidOperationException(
				"The requested physical Role arrangement is not available.");
		}

		return ArrangeKnownRole(playerId, role);
	}

	internal GameTestBuilder ArrangePubliclyRevealedRole(
		Guid playerId,
		MainRoleType role)
	{
		EnsureGameStarted();
		GetMutableSessionForArrangement().RevealRoles(
			new Dictionary<Guid, MainRoleType>
			{
				[playerId] = role
			});
		return this;
	}

		internal GameTestBuilder ArrangeEliminatedPlayer(
			Guid playerId,
			EliminationReason reason = EliminationReason.EventElimination)
	{
		EnsureGameStarted();
			GetMutableSessionForArrangement().EliminatePlayer(playerId, reason);
			return this;
		}

		internal GameTestBuilder ArrangeVotingRight(
			Guid playerId,
			bool hasVotingRight)
		{
			EnsureGameStarted();
			GetMutableSessionForArrangement().SetPlayerVotingRight(
				playerId,
				hasVotingRight);
			return this;
		}

	internal GameTestBuilder ArrangeCommittedWitchPotion(
		Guid witchId,
		Guid resourceId,
		NightActionType actionType,
		Guid targetId,
		Guid? powerInstanceId = null,
		RolePowerInstanceOrigin powerInstanceOrigin =
			RolePowerInstanceOrigin.Native,
		Guid? actingPlayerId = null,
		MainRoleType sourceRole = MainRoleType.Witch,
		string sourcePowerIdentifier = "witch-potions")
	{
		var identity = new OneUseRolePowerResourceIdentity(
			actingPlayerId ?? witchId,
			sourceRole,
			sourcePowerIdentifier,
			powerInstanceId ?? witchId,
			powerInstanceOrigin,
			resourceId);
		return ArrangeCommittedOneUseRolePower(
			identity,
			actionType,
			targetId);
	}

	internal GameTestBuilder ArrangeCommittedOneUseRolePower(
		OneUseRolePowerResourceIdentity resourceIdentity,
		NightActionType actionType,
		Guid targetId)
	{
		EnsureGameStarted();
		GetMutableSessionForArrangement().CommitOneUseRolePowerNightAction(
			actionType,
			targetId,
			resourceIdentity);
		return this;
	}

	internal GameTestBuilder ArrangeCurrentRole(
		Guid playerId,
		MainRoleType role)
	{
		EnsureGameStarted();
		GetMutableSessionForArrangement().AssignRole(playerId, role);
		return this;
	}

	internal GameTestBuilder ArrangeStatusEffect(
		Guid playerId,
		StatusEffectTypes effect)
	{
		EnsureGameStarted();
		GetMutableSessionForArrangement().ApplyStatusEffect(effect, playerId);
		return this;
	}

	internal GameTestBuilder ArrangeExplicitFactionTransition(
		string transitionIdentifier,
		params FactionFact[] facts)
	{
		EnsureGameStarted();
		_gameService.CommitExplicitFactionTransition(
			_gameId,
			transitionIdentifier,
			facts);
		return this;
	}

	internal GameTestBuilder ArrangeKnownWerewolfFactionAgentGroup(
		params Guid[] agentIds)
	{
		EnsureGameStarted();
		var session = GetGameState()
			?? throw new InvalidOperationException(
				CoreTestReferences.ExceptionMessages.GameMustBeStartedFirst);
		var playerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToHashSet();
		var knownAgentIds = agentIds.ToHashSet();
		if (knownAgentIds.Count != agentIds.Length ||
		    !knownAgentIds.IsSubsetOf(playerIds))
		{
			throw new ArgumentException(
				"Werewolf Faction Agents must be distinct Players in the Game Session.",
				nameof(agentIds));
		}

		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		var facts = playerIds
			.Select(playerId => FactionFact.Agent(
				playerId,
				Faction.Werewolf,
				knownAgentIds.Contains(playerId)
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent,
				boundary))
			.ToArray();

		return ArrangeExplicitFactionTransition(
			"test-known-werewolf-faction-agent-group",
			facts);
	}

	internal GameTestBuilder ArrangeNightAction(
		NightActionType actionType,
		Guid targetId)
	{
		EnsureGameStarted();
		GetMutableSessionForArrangement().PerformNightAction(
			actionType,
			targetId);
		return this;
	}

	internal GameTestBuilder ArrangeDayAction(DayPowerType actionType)
	{
		EnsureGameStarted();
		GetMutableSessionForArrangement()
			.PerformDayActionNoTarget(actionType);
		return this;
	}

    /// <summary>
    /// Confirms the game start and transitions to Night phase.
    /// </summary>
    public ProcessResult ConfirmGameStart()
    {
        EnsureGameStarted();
        var instruction = _lastInstruction as StartGameConfirmationInstruction
            ?? throw new InvalidOperationException(CoreTestReferences.ExceptionMessages.LastInstructionNotStartGameConfirmation);
        var response = instruction.CreateResponse();
		return _gameService.ProcessInstruction(_gameId, response);
    }

    /// <summary>
    /// Processes a moderator response and returns the result.
    /// </summary>
    public ProcessResult Process(ModeratorResponse response)
    {
        EnsureGameStarted();
        return _gameService.ProcessInstruction(_gameId, response);
    }

    /// <summary>
    /// Gets the current game state.
    /// </summary>
    public IGameSession? GetGameState() => _gameService.GetGameStateView(_gameId);

    public string SerializeSession() =>
        _gameService.SerializeSession(_gameId);

    /// <summary>
    /// Gets the current pending instruction.
    /// </summary>
    public ModeratorInstruction? GetCurrentInstruction() => _gameService.GetCurrentInstruction(_gameId);

    /// <summary>
    /// Gets the game ID.
    /// </summary>
    public Guid GameId => _gameId;

    /// <summary>
    /// Gets the underlying game service for advanced scenarios.
    /// </summary>
    public GameService GameService => _gameService;

    /// <summary>
    /// Gets player names in seating order.
    /// </summary>
    public IReadOnlyList<string> PlayerNames => _playerNames;

    /// <summary>
    /// Gets roles in play.
    /// </summary>
    public IReadOnlyList<MainRoleType> Roles => _roles;

    /// <summary>
    /// Gets the formatted diagnostic log of all state changes.
    /// </summary>
    public string DiagnosticLog => _diagnosticObserver.GetFormattedLog();

    /// <summary>
    /// Gets the raw observer log entries for assertions.
    /// </summary>
    public IReadOnlyList<string> ObserverLog => _diagnosticObserver.Log;

    /// <summary>
    /// Clears the observer log (useful for focusing on specific transitions).
    /// </summary>
    public void ClearObserverLog() => _diagnosticObserver.Clear();

    /// <summary>
    /// Writes the diagnostic log to the test output.
    /// </summary>
    public void DumpDiagnostics() => _output?.WriteLine(DiagnosticLog);

    #region Night Phase Helpers

    /// <summary>
    /// Confirms the "night starts" instruction that precedes the hook loop.
    /// </summary>
    /// <returns>The result of processing the night start confirmation.</returns>
    public ProcessResult ConfirmNightStart()
    {
        EnsureGameStarted();
        var nightStartInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.NightStartConfirmation);
        var response = nightStartInstruction.CreateResponse();
        return Process(response);
    }

    /// <summary>
    /// Completes the Werewolf collective Night Action sequence:
    /// observe or wake the Agent group → select victim → confirm sleep.
    /// </summary>
    /// <param name="werewolfIds">The IDs of all living Werewolf Faction Agents.</param>
    /// <param name="victimId">The ID of the player to select as the victim.</param>
    /// <returns>The result of the final sleep confirmation.</returns>
    public ProcessResult CompleteWerewolfNightAction(HashSet<Guid> werewolfIds, Guid victimId)
    {
        EnsureGameStarted();

        var afterWake = CompleteWerewolfWakeOrObservation(werewolfIds);

        // Select victim
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterWake,
            CoreTestReferences.InstructionContexts.WerewolfVictimSelection);
        var victimResponse = victimInstruction.CreateResponse([victimId]);
        var afterVictim = Process(victimResponse);

        // Confirm sleep
        var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVictim,
            CoreTestReferences.InstructionContexts.WerewolfSleepConfirmation);
        var sleepResponse = sleepInstruction.CreateResponse();
        return Process(sleepResponse);
    }

    /// <summary>
    /// Completes the werewolf night action sequence for Night 2+: wakeup → select victim → confirm sleep.
    /// Unlike CompleteWerewolfNightAction, this requires the complete living
    /// Agent group to have been observed already.
    /// </summary>
    /// <param name="victimId">The ID of the player to select as the victim.</param>
    /// <returns>The result of the final sleep confirmation.</returns>
    public ProcessResult CompleteWerewolfNightActionSubsequentNight(Guid victimId)
    {
        EnsureGameStarted();

        // Confirm wake up (the complete Agent group is already known)
        var wakeupInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.WerewolfWakeConfirmation);
        if (wakeupInstruction.Semantic != ModeratorInstructionSemantic.WakeRole)
        {
            throw new AssertionException(
                $"Expected {ModeratorInstructionSemantic.WakeRole}, but received {wakeupInstruction.Semantic}.");
        }

        var afterWakeup = Process(wakeupInstruction.CreateResponse());

        // Select victim
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterWakeup,
            CoreTestReferences.InstructionContexts.WerewolfVictimSelection);
        var victimResponse = victimInstruction.CreateResponse([victimId]);
        var afterVictim = Process(victimResponse);

        // Confirm sleep
        var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVictim,
            CoreTestReferences.InstructionContexts.WerewolfSleepConfirmation);
        var sleepResponse = sleepInstruction.CreateResponse();
        return Process(sleepResponse);
    }

    /// <summary>
    /// Completes the Seer night action sequence: identify → select target → confirm sleep.
    /// </summary>
    /// <param name="seerId">The ID of the Seer player to identify.</param>
    /// <param name="targetId">The ID of the player for the Seer to investigate.</param>
    /// <returns>The result of the final sleep confirmation.</returns>
    public ProcessResult CompleteSeerNightAction(Guid seerId, Guid targetId)
    {
        EnsureGameStarted();

        // Identify seer
        var identifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.SeerIdentification);
        var identifyResponse = identifyInstruction.CreateResponse([seerId]);
        var afterIdentify = Process(identifyResponse);

        // Select target
        var targetInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            CoreTestReferences.InstructionContexts.SeerTargetSelection);
        var targetResponse = targetInstruction.CreateResponse([targetId]);
        var afterTarget = Process(targetResponse);

        // Confirm result given to player

        var resultInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterTarget,
            CoreTestReferences.InstructionContexts.SeerResultConfirmation);
        var resultResponse = resultInstruction.CreateResponse();
        var afterResult = Process(resultResponse);

		// Confirm sleep
		var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterResult,
            CoreTestReferences.InstructionContexts.SeerSleepConfirmation);
        var sleepResponse = sleepInstruction.CreateResponse();
        return Process(sleepResponse);
    }

    /// <summary>
    /// Completes the Accursed Wolf-Father night action sequence:
    /// identify and wake → choose whether to infect → confirm sleep.
    /// </summary>
    /// <param name="accursedWolfFatherId">The ID of the Accursed Wolf-Father player.</param>
    /// <param name="infectsVictim">Whether to infect the retained collective victim.</param>
    /// <returns>The result of the final sleep confirmation.</returns>
    public ProcessResult CompleteAccursedWolfFatherNightAction(
        Guid accursedWolfFatherId,
        bool infectsVictim)
    {
        EnsureGameStarted();

        ProcessResult afterWake;
        switch (GetCurrentInstruction())
        {
            case SelectPlayersInstruction
            {
                Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
                RoleIdentification: MainRoleType.AccursedWolfFather
            } identify:
            {
                var afterIdentify = Process(
                    identify.CreateResponse([accursedWolfFatherId]));
                if (afterIdentify.ModeratorInstruction is SelectOptionsInstruction
                    {
                        Semantic:
                            ModeratorInstructionSemantic
                                .ChooseAccursedWolfFatherInfection
                    })
                {
                    afterWake = afterIdentify;
                    break;
                }

                var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                    afterIdentify,
                    "Accursed Wolf-Father wake confirmation");
                if (wake.Semantic != ModeratorInstructionSemantic.WakeRole ||
                    wake.AffectedPlayerIds is not [var affectedPlayerId] ||
                    affectedPlayerId != accursedWolfFatherId)
                {
                    throw new AssertionException(
                        "Expected the identified Accursed Wolf-Father to receive the wake confirmation.");
                }

                afterWake = Process(wake.CreateResponse());
                break;
            }
            case ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.WakeRole,
                AffectedPlayerIds: [var affectedPlayerId]
            } wake when affectedPlayerId == accursedWolfFatherId:
                afterWake = Process(wake.CreateResponse());
                break;
            case null:
                throw new InvalidOperationException(
                    "No current instruction is available for the Accursed Wolf-Father wake.");
            case var instruction:
                throw new AssertionException(
                    $"Expected an Accursed Wolf-Father identification or wake instruction, but received " +
                    $"{instruction.GetType().Name} ({instruction.Semantic}).");
        }

        var choice = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
            afterWake,
            "Accursed Wolf-Father infection choice");
        if (choice.Semantic !=
            ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection)
        {
            throw new AssertionException(
                $"Expected {ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection}, " +
                $"but received {choice.Semantic}.");
        }

        var optionId = infectsVictim
            ? AccursedWolfFatherInfectionOptionIds.Infect
            : AccursedWolfFatherInfectionOptionIds.Decline;
        var afterChoice = Process(choice.CreateResponse(optionId));

        var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterChoice,
            "Accursed Wolf-Father sleep confirmation");
        if (sleep.Semantic != ModeratorInstructionSemantic.PutRoleToSleep ||
            sleep.AffectedPlayerIds is not [var sleepingPlayerId] ||
            sleepingPlayerId != accursedWolfFatherId)
        {
            throw new AssertionException(
                "Expected the Accursed Wolf-Father to receive the sleep confirmation.");
        }

        return Process(sleep.CreateResponse());
    }

    /// <summary>
    /// Completes the Big Bad Wolf night action sequence:
    /// identify or wake → select the additional victim → confirm sleep.
    /// </summary>
    public ProcessResult CompleteBigBadWolfNightAction(
        Guid bigBadWolfId,
        Guid targetId)
    {
        EnsureGameStarted();

        ProcessResult afterWake;
        switch (GetCurrentInstruction())
        {
            case SelectPlayersInstruction
            {
                Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
                RoleIdentification: MainRoleType.BigBadWolf
            } identify:
                afterWake = Process(
                    identify.CreateResponse([bigBadWolfId]));
                break;
            case ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.WakeRole,
                AffectedPlayerIds: [var affectedPlayerId]
            } wake when affectedPlayerId == bigBadWolfId:
                afterWake = Process(wake.CreateResponse());
                break;
            case SelectPlayersInstruction
            {
                Semantic:
                    ModeratorInstructionSemantic.SelectBigBadWolfTarget
            } targetSelection:
                afterWake = ProcessResult.Success(targetSelection);
                break;
            case null:
                throw new InvalidOperationException(
                    "No current instruction is available for the Big Bad Wolf wake.");
            case var instruction:
                throw new AssertionException(
                    $"Expected a Big Bad Wolf identification or wake instruction, but received " +
                    $"{instruction.GetType().Name} ({instruction.Semantic}).");
        }

        var target = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterWake,
            "Big Bad Wolf target selection");
        if (target.Semantic !=
            ModeratorInstructionSemantic.SelectBigBadWolfTarget)
        {
            throw new AssertionException(
                $"Expected {ModeratorInstructionSemantic.SelectBigBadWolfTarget}, " +
                $"but received {target.Semantic}.");
        }

        var afterTarget = Process(target.CreateResponse([targetId]));
        var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterTarget,
            "Big Bad Wolf sleep confirmation");
        if (sleep.Semantic != ModeratorInstructionSemantic.PutRoleToSleep ||
            sleep.AffectedPlayerIds is not [var sleepingPlayerId] ||
            sleepingPlayerId != bigBadWolfId)
        {
            throw new AssertionException(
                "Expected the Big Bad Wolf to receive the sleep confirmation.");
        }

        return Process(sleep.CreateResponse());
    }

	/// <summary>
	/// Completes the Actor night action sequence:
	/// identify or wake → choose or decline a setup card → confirm sleep.
	/// </summary>
	/// <param name="actorId">The ID of the Actor player.</param>
	/// <param name="setupCardId">The setup card to borrow, or null to decline.</param>
	/// <returns>The result of the final sleep confirmation.</returns>
	public ProcessResult CompleteActorNightAction(
		Guid actorId,
		Guid? setupCardId)
	{
		EnsureGameStarted();

		var afterWakeOrIdentification = GetCurrentInstruction() switch
		{
			SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.IdentifyRoleHolders,
				RoleIdentification: MainRoleType.Actor
			} identify => Process(identify.CreateResponse([actorId])),
			ConfirmationInstruction
			{
				Semantic: ModeratorInstructionSemantic.WakeRole,
				AffectedPlayerIds: [var affectedPlayerId]
			} wake when affectedPlayerId == actorId => Process(wake.CreateResponse()),
			null => throw new InvalidOperationException(
				"No current instruction is available for the Actor wake."),
			var instruction => throw new AssertionException(
				$"Expected an Actor identification or wake instruction, but received " +
				$"{instruction.GetType().Name} ({instruction.Semantic}).")
		};

		var choice = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			afterWakeOrIdentification,
			"Actor setup-card choice");
		if (choice.Semantic != ModeratorInstructionSemantic.ChooseActorSetupCard)
		{
			throw new AssertionException(
				$"Expected {ModeratorInstructionSemantic.ChooseActorSetupCard}, " +
				$"but received {choice.Semantic}.");
		}

		var afterChoice = Process(setupCardId is { } selectedSetupCardId
			? choice.CreateResponse(selectedSetupCardId.ToString("D"))
			: choice.CreateResponse());
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterChoice,
			"Actor sleep confirmation");
		if (sleep.Semantic != ModeratorInstructionSemantic.PutRoleToSleep)
		{
			throw new AssertionException(
				$"Expected {ModeratorInstructionSemantic.PutRoleToSleep}, " +
				$"but received {sleep.Semantic}.");
		}

		return Process(sleep.CreateResponse());
	}

    /// <summary>
    /// Completes a full night phase by iterating through roles in the order defined by HookListeners[NightMainActionLoop].
    /// This includes confirming the night-end instruction that transitions to Dawn.
    /// </summary>
    /// <param name="inputs">The inputs for each role's night actions.</param>
    /// <returns>The result of the final action in the night phase.</returns>
    public ProcessResult CompleteNightPhase(NightActionInputs inputs)
    {
        EnsureGameStarted();

        // Confirm night starts
        ConfirmNightStart();

        ProcessResult result = ProcessResult.Success(GetCurrentInstruction()!);

        // Iterate through roles in the order defined by HookListeners
        var nightListeners = GameFlowManager.HookListeners[NightMainActionLoop];
        
        foreach (var listener in nightListeners)
        {
            // Only process main roles (not secondary roles or events)
            if (listener.ListenerType != GameHookListenerType.MainRole)
                continue;

            // Check if this role has an implementation
            if (!GameFlowManager.ListenerFactories.ContainsKey(listener))
                continue;

            // Parse the role type
            MainRoleType roleType = listener;

            // Handle each role's night action based on the provided inputs
            result = roleType switch
            {
				MainRoleType.Actor => HandleActorNightAction(inputs),
                MainRoleType.SimpleWerewolf => HandleWerewolfNightAction(inputs),
                MainRoleType.Seer => HandleSeerNightAction(inputs),
                MainRoleType.AccursedWolfFather =>
                    HandleAccursedWolfFatherNightAction(inputs),
                MainRoleType.BigBadWolf =>
                    HandleBigBadWolfNightAction(inputs),
                // Future roles can be added here as they're implemented:
                // MainRoleType.Witch => HandleWitchNightAction(inputs),
                // MainRoleType.Defender => HandleDefenderNightAction(inputs),
                _ => result // Role not handled yet, skip
            };

            if (!result.IsSuccess)
                return result;
        }

        // Confirm the night-end instruction that transitions out of night actions.
        // This transitions the game to Dawn phase proper
        var nightEndInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            result,
            CoreTestReferences.InstructionContexts.NightEndConfirmation);
        result = Process(nightEndInstruction.CreateResponse());

        return result;
    }

    /// <summary>
    /// Completes a full night phase with werewolf and optional Seer actions.
    /// This is a convenience overload that creates NightActionInputs from individual parameters.
    /// </summary>
    /// <param name="werewolfIds">The IDs of all werewolf players.</param>
    /// <param name="victimId">The ID of the werewolf victim.</param>
    /// <param name="seerId">Optional: The ID of the Seer player. If null, Seer actions are skipped.</param>
    /// <param name="seerTargetId">Optional: The ID of the player for the Seer to investigate. Required if seerId is provided.</param>
    /// <returns>The result of the final action in the night phase.</returns>
    public ProcessResult CompleteNightPhase(HashSet<Guid> werewolfIds, Guid victimId, Guid? seerId = null, Guid? seerTargetId = null)
    {
        if (seerId.HasValue && !seerTargetId.HasValue)
            throw new ArgumentException(CoreTestReferences.ExceptionMessages.SeerTargetRequiredWithSeer, nameof(seerTargetId));

        var inputs = new NightActionInputs
        {
            WerewolfIds = werewolfIds,
            WerewolfVictimId = victimId,
            SeerId = seerId,
            SeerTargetId = seerTargetId
        };

        return CompleteNightPhase(inputs);
    }

    /// <summary>
    /// Handles the werewolf night action if inputs are provided.
    /// </summary>
    private ProcessResult HandleWerewolfNightAction(NightActionInputs inputs)
    {
        if (inputs.WerewolfIds == null || inputs.WerewolfVictimId == null)
            return ProcessResult.Success(GetCurrentInstruction()!);

        return CompleteWerewolfNightAction(inputs.WerewolfIds, inputs.WerewolfVictimId.Value);
    }

    private ProcessResult CompleteWerewolfWakeOrObservation(
        HashSet<Guid> werewolfAgentIds)
    {
        return GetCurrentInstruction() switch
        {
            SelectPlayersInstruction
            {
                Semantic:
                    ModeratorInstructionSemantic
                        .ObserveWerewolfFactionAgentGroup
            } observation =>
                Process(observation.CreateResponse(werewolfAgentIds)),
            ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.WakeRole
            } wake =>
                Process(wake.CreateResponse()),
            null => throw new InvalidOperationException(
                "No current instruction is available for the Werewolf collective wake."),
            var instruction => throw new AssertionException(
                $"Expected a Werewolf collective wake or Agent-group observation, but received " +
                $"{instruction.GetType().Name} ({instruction.Semantic}).")
        };
    }

    /// <summary>
    /// Handles the Seer night action if inputs are provided.
    /// </summary>
    private ProcessResult HandleSeerNightAction(NightActionInputs inputs)
    {
        if (inputs.SeerId == null || inputs.SeerTargetId == null)
            return ProcessResult.Success(GetCurrentInstruction()!);

        return CompleteSeerNightAction(inputs.SeerId.Value, inputs.SeerTargetId.Value);
    }

    /// <summary>
    /// Handles the Accursed Wolf-Father night action if inputs are provided.
    /// </summary>
    private ProcessResult HandleAccursedWolfFatherNightAction(NightActionInputs inputs)
    {
        if (inputs.AccursedWolfFatherId == null ||
            inputs.AccursedWolfFatherInfectsVictim == null)
        {
            return ProcessResult.Success(GetCurrentInstruction()!);
        }

        return CompleteAccursedWolfFatherNightAction(
            inputs.AccursedWolfFatherId.Value,
            inputs.AccursedWolfFatherInfectsVictim.Value);
    }

    /// <summary>
    /// Handles the Big Bad Wolf night action if inputs are provided.
    /// </summary>
    private ProcessResult HandleBigBadWolfNightAction(NightActionInputs inputs)
    {
        if (inputs.BigBadWolfId == null ||
            inputs.BigBadWolfTargetId == null)
        {
            return ProcessResult.Success(GetCurrentInstruction()!);
        }

        return CompleteBigBadWolfNightAction(
            inputs.BigBadWolfId.Value,
            inputs.BigBadWolfTargetId.Value);
    }

	/// <summary>
	/// Handles the Actor night action if an Actor ID is provided.
	/// A null setup-card ID records the Actor declining the optional choice.
	/// </summary>
	private ProcessResult HandleActorNightAction(NightActionInputs inputs)
	{
		if (inputs.ActorId == null)
			return ProcessResult.Success(GetCurrentInstruction()!);

		return CompleteActorNightAction(
			inputs.ActorId.Value,
			inputs.ActorSetupCardId);
	}

    #endregion

    #region Dawn Phase Helpers

    /// <summary>
    /// Completes the dawn phase flow: CalculateVictims → AnnounceVictims (with role assignments) → DawnMainActionLoop → Finalize → Day.
    /// Handles variable number of victims (0 to many) by processing instructions until Day phase is reached.
    /// </summary>
    /// <param name="roleAssignments">Optional: Specific role assignments for eliminated players. If null, assigns SimpleVillager to all.</param>
    /// <returns>The result of the final instruction that transitions to Day phase.</returns>
    public ProcessResult CompleteDawnPhase(Dictionary<Guid, MainRoleType>? roleAssignments = null)
    {
        EnsureGameStarted();

        ProcessResult result;

        // Process instructions until we reach Day phase
        while (true)
        {
            var instruction = GetCurrentInstruction();
            var currentPhase = GetGameState()?.GetCurrentPhase();

            // If we've reached Day phase, we're done
            if (currentPhase == GamePhase.Day)
            {
                // Return a success result with the current instruction
                return ProcessResult.Success(instruction!);
            }

            // Handle different instruction types during dawn
            result = instruction switch
            {
                AssignRolesInstruction assignRoles => HandleAssignRolesInstruction(assignRoles, roleAssignments),
                ConfirmationInstruction confirmation => Process(confirmation.CreateResponse()),
                SelectPlayersInstruction selectPlayers => throw new InvalidOperationException(
                    CoreTestReferences.ExceptionMessages.UnexpectedSelectPlayersDuringDawnPhase(selectPlayers.PrivateInstruction)),
                null => throw new InvalidOperationException(CoreTestReferences.ExceptionMessages.NoCurrentInstructionDuringDawnPhase),
                _ => throw new InvalidOperationException(
                    CoreTestReferences.ExceptionMessages.UnexpectedInstructionTypeDuringDawnPhase(instruction.GetType().Name))
            };

            if (!result.IsSuccess)
            {
                return result;
            }
        }
    }

    /// <summary>
    /// Handles AssignRolesInstruction using a complete physically observed mapping.
    /// </summary>
    /// <param name="instruction">The role assignment instruction.</param>
    /// <param name="overrideAssignments">The complete observed mapping.</param>
    private ProcessResult HandleAssignRolesInstruction(AssignRolesInstruction instruction, Dictionary<Guid, MainRoleType>? overrideAssignments = null)
    {
		if (instruction.PlayersForAssignment.Count == 0)
		{
			return Process(instruction.CreateResponse([]));
		}

        if (overrideAssignments is null ||
            instruction.PlayersForAssignment.Any(playerId =>
                !overrideAssignments.ContainsKey(playerId)))
        {
			throw new InvalidOperationException(
				CoreTestReferences.ExceptionMessages
					.ObservedRoleAssignmentsRequired);
        }

        var assignments = instruction.PlayersForAssignment.ToDictionary(
            playerId => playerId,
            playerId => overrideAssignments[playerId]);
        var response = instruction.CreateResponse(assignments);
        return Process(response);
    }

    #endregion

    #region Day Phase Helpers

    /// <summary>
    /// Completes the day phase with a player being lynched.
    /// Flow: Debate → DetermineVoteType → NormalVoting → ProcessVoteOutcome → RoleAssignment → Finalize → Night.
    /// </summary>
    /// <param name="lynchTargetId">The ID of the player to be lynched.</param>
    /// <param name="roleAssignments">The complete observed mapping when the target's Role is unknown.</param>
    /// <returns>The result of the final instruction that transitions to Night phase.</returns>
    public ProcessResult CompleteDayPhaseWithLynch(
        Guid lynchTargetId,
        Dictionary<Guid, MainRoleType>? roleAssignments = null)
    {
        return CompleteDayPhaseCore(lynchTargetId, roleAssignments);
    }

    /// <summary>
    /// Completes the day phase with a tie vote (no elimination).
    /// Flow: Debate → DetermineVoteType → NormalVoting → ProcessVoteOutcome → Finalize → Night.
    /// </summary>
    /// <returns>The result of the final instruction that transitions to Night phase.</returns>
    public ProcessResult CompleteDayPhaseWithTie()
    {
        return CompleteDayPhaseCore(null, roleAssignments: null);
    }

    /// <summary>
    /// Core implementation for completing the day phase.
    /// Handles both lynch and tie scenarios by processing instructions until Night phase is reached.
    /// </summary>
    /// <param name="lynchTargetId">The ID of the player to lynch, or null for a tie vote.</param>
    /// <param name="roleAssignments">The complete observed mapping when a reveal requires one.</param>
    /// <returns>The result of the final instruction that transitions to Night phase.</returns>
    private ProcessResult CompleteDayPhaseCore(
        Guid? lynchTargetId,
        Dictionary<Guid, MainRoleType>? roleAssignments)
    {
        EnsureGameStarted();

        ProcessResult result;

        // Process instructions until we reach Night phase
        while (true)
        {
            var instruction = GetCurrentInstruction();
            var currentPhase = GetGameState()?.GetCurrentPhase();

            // If we've reached Night phase, we're done
            if (currentPhase == GamePhase.Night)
            {
                // Return a success result with the current instruction
                return ProcessResult.Success(instruction!);
            }

            // Handle different instruction types during day phase
            result = instruction switch
            {
                SelectPlayersInstruction selectPlayers => HandleDayVotingInstruction(selectPlayers, lynchTargetId),
                AssignRolesInstruction assignRoles => HandleAssignRolesInstruction(
                    assignRoles,
                    roleAssignments),
                ConfirmationInstruction confirmation => Process(confirmation.CreateResponse()),
                null => throw new InvalidOperationException(CoreTestReferences.ExceptionMessages.NoCurrentInstructionDuringDayPhase),
                _ => throw new InvalidOperationException(
                    CoreTestReferences.ExceptionMessages.UnexpectedInstructionTypeDuringDayPhase(instruction.GetType().Name))
            };

            if (!result.IsSuccess)
            {
                return result;
            }
        }
    }

    /// <summary>
    /// Handles SelectPlayersInstruction during day voting.
    /// Selects the lynch target if provided, otherwise selects no players (tie).
    /// </summary>
    private ProcessResult HandleDayVotingInstruction(SelectPlayersInstruction instruction, Guid? lynchTargetId)
    {
        var selectedPlayers = lynchTargetId.HasValue
            ? new HashSet<Guid> { lynchTargetId.Value }
            : new HashSet<Guid>();

        var response = instruction.CreateResponse(selectedPlayers);
        return Process(response);
    }

    #endregion

    private void EnsureGameStarted()
    {
        if (!_gameStarted)
            throw new InvalidOperationException(CoreTestReferences.ExceptionMessages.GameMustBeStartedFirst);
    }

	private GameSession GetMutableSessionForArrangement() =>
		(GameSession)(GetGameState()
			?? throw new InvalidOperationException(
				CoreTestReferences.ExceptionMessages.GameMustBeStartedFirst));
}
