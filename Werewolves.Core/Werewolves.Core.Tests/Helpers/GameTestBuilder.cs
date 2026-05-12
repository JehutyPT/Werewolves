using Werewolves.Core.GameLogic.Models.InternalMessages;
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
    /// Werewolf action: IDs of werewolf players to identify.
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
    private readonly GameService _gameService = new();
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

    /// <summary>
    /// Creates a simple game with werewolves, seer, and villagers.
    /// </summary>
    /// <param name="playerCount">Total players (minimum 4)</param>
    /// <param name="werewolfCount">Number of werewolves (default 1)</param>
    /// <param name="includeSeer">Include a seer (default true)</param>
    public GameTestBuilder WithSimpleGame(int playerCount, int werewolfCount = 1, bool includeSeer = true)
    {
        if (playerCount < 3)
            throw new ArgumentException("Minimum 3 players required", nameof(playerCount));

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
                $"Player count ({_playerNames.Count}) must match role count ({_roles.Count})");

        var instruction = _gameService.StartNewGameWithObserver(_playerNames, _roles, stateChangeObserver: _diagnosticObserver);
        _lastInstruction = instruction;
		_gameId = instruction.GameGuid;
        _gameStarted = true;
        
        // Wire up session for GUID-to-name resolution in diagnostics
        var session = _gameService.GetGameStateView(_gameId);
        if (session != null)
            _diagnosticObserver.SetSession(session);
        
        return instruction;
    }

    /// <summary>
    /// Confirms the game start and transitions to Night phase.
    /// </summary>
    public ProcessResult ConfirmGameStart()
    {
        EnsureGameStarted();
        var instruction = _lastInstruction as StartGameConfirmationInstruction
            ?? throw new InvalidOperationException("Last instruction is not a StartGameConfirmationInstruction");
        var response = instruction.CreateResponse(true);
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
            "Night start confirmation");
        var response = nightStartInstruction.CreateResponse(true);
        return Process(response);
    }

    /// <summary>
    /// Completes the werewolf night action sequence: identify → select victim → confirm sleep.
    /// </summary>
    /// <param name="werewolfIds">The IDs of all werewolf players to identify.</param>
    /// <param name="victimId">The ID of the player to select as the victim.</param>
    /// <returns>The result of the final sleep confirmation.</returns>
    public ProcessResult CompleteWerewolfNightAction(HashSet<Guid> werewolfIds, Guid victimId)
    {
        EnsureGameStarted();

        // Identify werewolves
        var identifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            GetCurrentInstruction(),
            "Werewolf identification");
        var identifyResponse = identifyInstruction.CreateResponse(werewolfIds);
        var afterIdentify = Process(identifyResponse);

        // Select victim
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Werewolf victim selection");
        var victimResponse = victimInstruction.CreateResponse([victimId]);
        var afterVictim = Process(victimResponse);

        // Confirm sleep
        var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVictim,
            "Werewolf sleep confirmation");
        var sleepResponse = sleepInstruction.CreateResponse(true);
        return Process(sleepResponse);
    }

    /// <summary>
    /// Completes the werewolf night action sequence for Night 2+: wakeup → select victim → confirm sleep.
    /// Unlike CompleteWerewolfNightAction, this does not expect identification (already done on Night 1).
    /// </summary>
    /// <param name="victimId">The ID of the player to select as the victim.</param>
    /// <returns>The result of the final sleep confirmation.</returns>
    public ProcessResult CompleteWerewolfNightActionSubsequentNight(Guid victimId)
    {
        EnsureGameStarted();

        // Confirm wake up (no identification needed after Night 1)
        var wakeupInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            GetCurrentInstruction(),
            "Werewolf wake up confirmation");
        var afterWakeup = Process(wakeupInstruction.CreateResponse(true));

        // Select victim
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterWakeup,
            "Werewolf victim selection");
        var victimResponse = victimInstruction.CreateResponse([victimId]);
        var afterVictim = Process(victimResponse);

        // Confirm sleep
        var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterVictim,
            "Werewolf sleep confirmation");
        var sleepResponse = sleepInstruction.CreateResponse(true);
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
            "Seer identification");
        var identifyResponse = identifyInstruction.CreateResponse([seerId]);
        var afterIdentify = Process(identifyResponse);

        // Select target
        var targetInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Seer target selection");
        var targetResponse = targetInstruction.CreateResponse([targetId]);
        var afterTarget = Process(targetResponse);

        // Confirm result given to player

        var resultInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterTarget,
            "Seer result confirmation");
        var resultResponse = resultInstruction.CreateResponse(true);
        var afterResult = Process(resultResponse);

		// Confirm sleep
		var sleepInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            afterTarget,
            "Seer sleep confirmation");
        var sleepResponse = sleepInstruction.CreateResponse(true);
        return Process(sleepResponse);
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
                MainRoleType.SimpleWerewolf => HandleWerewolfNightAction(inputs),
                MainRoleType.Seer => HandleSeerNightAction(inputs),
                // Future roles can be added here as they're implemented:
                // MainRoleType.Witch => HandleWitchNightAction(inputs),
                // MainRoleType.Defender => HandleDefenderNightAction(inputs),
                _ => result // Role not handled yet, skip
            };

            if (!result.IsSuccess)
                return result;
        }

        // Confirm the night-end instruction ("Night actions complete. Village wakes up.")
        // This transitions the game to Dawn phase proper
        var nightEndInstruction = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
            result,
            "Night end confirmation");
        result = Process(nightEndInstruction.CreateResponse(true));

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
            throw new ArgumentException("seerTargetId must be provided when seerId is specified", nameof(seerTargetId));

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

    /// <summary>
    /// Handles the Seer night action if inputs are provided.
    /// </summary>
    private ProcessResult HandleSeerNightAction(NightActionInputs inputs)
    {
        if (inputs.SeerId == null || inputs.SeerTargetId == null)
            return ProcessResult.Success(GetCurrentInstruction()!);

        return CompleteSeerNightAction(inputs.SeerId.Value, inputs.SeerTargetId.Value);
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
                ConfirmationInstruction confirmation => Process(confirmation.CreateResponse(true)),
                SelectPlayersInstruction selectPlayers => throw new InvalidOperationException(
                    $"Unexpected SelectPlayersInstruction during dawn phase. " +
                    $"Dawn hooks requiring player selection are not handled by CompleteDawnPhase(). " +
                    $"Instruction: {selectPlayers.PrivateInstruction}"),
                null => throw new InvalidOperationException("No current instruction available during dawn phase processing."),
                _ => throw new InvalidOperationException(
                    $"Unexpected instruction type during dawn phase: {instruction.GetType().Name}")
            };

            if (!result.IsSuccess)
            {
                return result;
            }
        }
    }

    /// <summary>
    /// Handles AssignRolesInstruction by using provided assignments or defaulting to SimpleVillager.
    /// </summary>
    /// <param name="instruction">The role assignment instruction.</param>
    /// <param name="overrideAssignments">Optional specific assignments. Missing players get SimpleVillager.</param>
    private ProcessResult HandleAssignRolesInstruction(AssignRolesInstruction instruction, Dictionary<Guid, MainRoleType>? overrideAssignments = null)
    {
        var assignments = new Dictionary<Guid, MainRoleType>();
        
        foreach (var playerId in instruction.PlayersForAssignment)
        {
            if (overrideAssignments != null && overrideAssignments.TryGetValue(playerId, out var specifiedRole))
            {
                assignments[playerId] = specifiedRole;
            }
            else
            {
                // Default to first available role or SimpleVillager
                var roleToAssign = instruction.RolesForAssignment.FirstOrDefault(MainRoleType.SimpleVillager);
                assignments[playerId] = roleToAssign;
            }
        }

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
    /// <returns>The result of the final instruction that transitions to Night phase.</returns>
    public ProcessResult CompleteDayPhaseWithLynch(Guid lynchTargetId)
    {
        return CompleteDayPhaseCore(lynchTargetId);
    }

    /// <summary>
    /// Completes the day phase with a tie vote (no elimination).
    /// Flow: Debate → DetermineVoteType → NormalVoting → ProcessVoteOutcome → Finalize → Night.
    /// </summary>
    /// <returns>The result of the final instruction that transitions to Night phase.</returns>
    public ProcessResult CompleteDayPhaseWithTie()
    {
        return CompleteDayPhaseCore(null);
    }

    /// <summary>
    /// Core implementation for completing the day phase.
    /// Handles both lynch and tie scenarios by processing instructions until Night phase is reached.
    /// </summary>
    /// <param name="lynchTargetId">The ID of the player to lynch, or null for a tie vote.</param>
    /// <returns>The result of the final instruction that transitions to Night phase.</returns>
    private ProcessResult CompleteDayPhaseCore(Guid? lynchTargetId)
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
                AssignRolesInstruction assignRoles => HandleAssignRolesInstruction(assignRoles),
                ConfirmationInstruction confirmation => Process(confirmation.CreateResponse(true)),
                null => throw new InvalidOperationException("No current instruction available during day phase processing."),
                _ => throw new InvalidOperationException(
                    $"Unexpected instruction type during day phase: {instruction.GetType().Name}")
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
            throw new InvalidOperationException("Game must be started first. Call StartGame().");
    }
}
