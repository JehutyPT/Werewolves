// Added for IRole
// Added for specific roles
// For thread-safe storage
// Needed for Any()
// Add this line for resource access
// For Debug.Fail

using System.Collections.Concurrent;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using static Werewolves.Core.StateModels.Enums.ExpectedInputType;

namespace Werewolves.Core.GameLogic.Services;

/// <summary>
/// Orchestrates the game flow based on moderator input and tracked state using a state machine.
/// </summary>
public class GameService
{
	// Simple in-memory storage for game sessions. Replaceable with DI.
	private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();

	public GameService() {}

    public LobbySetupMetadata GetLobbySetupMetadata()
    {
	    return SupportedRoleCatalog.CreateLobbySetupMetadata();
    }

    public StartGameConfirmationInstruction StartNewGame(
        GameSessionConfig config) => StartNewGameCore(
            config,
            stateChangeObserver: null);

    // Overload to accept state change observer for test suite diagnostics
    internal StartGameConfirmationInstruction StartNewGameWithObserver(
        List<string> playerNamesInOrder, 
        List<MainRoleType> rolesInPlay, 
        List<string>? eventCardIdsInDeck = null,
        IStateChangeObserver? stateChangeObserver = null) => StartNewGameCore(
            new GameSessionConfig(playerNamesInOrder, rolesInPlay),
            stateChangeObserver);

    /// <summary>
    /// Restores a game session from its serialized representation and adds it to the active session collection.
    /// </summary>
    /// <param name="serializedSession">The serialized data representing the game session to be restored. Cannot be null or empty.</param>
    /// <returns>The unique ID of the rehydrated game session.</returns>
    public Guid RehydrateSession(string serializedSession)
    {
        var session = new GameSession(serializedSession);
        _sessions.TryAdd(session.Id, session);
        return session.Id;
	}

	/// <summary>
	/// Starts a new game session.
	/// </summary>
	/// <param name="playerNamesInOrder">List of player names in clockwise seating order.</param>
	/// <param name="rolesInPlay">List of RoleTypes included in the game.</param>
	/// <param name="eventCardIdsInDeck">Optional list of event card IDs included.</param>
	/// <param name="stateChangeObserver">Optional observer for state change diagnostics.</param>
	/// <returns>The unique ID for the newly created game session.</returns>
	private StartGameConfirmationInstruction StartNewGameCore(
        GameSessionConfig config, IStateChangeObserver? stateChangeObserver)
    {
	    EnforceRolesAreSupported(config.Roles);

        // 1. Generate the game ID
        var gameId = Guid.NewGuid();
        
        // 2. Get the initial instruction from GameFlowManager (pure function)
        var initialInstruction = GameFlowManager.GetInitialInstruction(config.Roles, gameId);
        
        // 3. Create the session with both the ID and instruction
        var session = new GameSession(gameId, initialInstruction, config, stateChangeObserver);
        
        // 4. Store the session
        _sessions.TryAdd(session.Id, session);
        
        // 5. Return the same instruction that was passed to the session
        return initialInstruction;
    }

    /// <summary>
    /// Retrieves the currently pending instruction for the moderator.
    /// </summary>
    /// <param name="gameId">The ID of the game session.</param>
    /// <returns>The pending instruction, or null if game not found or no instruction pending.</returns>
    public ModeratorInstruction? GetCurrentInstruction(Guid gameId)
    {
        if (_sessions.TryGetValue(gameId, out var session))
        {
            return session.PendingModeratorInstruction;
        }
        return null; // Or throw GameNotFoundException
    }

    /// <summary>
    /// Gets a view of the current game state.
    /// Basic implementation returns the session object itself (consider a DTO later).
    /// </summary>
    /// <param name="gameId">The ID of the game session.</param>
    /// <returns>The game session object, or null if not found.</returns>
    public IGameSession? GetGameStateView(Guid gameId)
    {
        _sessions.TryGetValue(gameId, out var session);
        return session; // Or throw GameNotFoundException, or return a dedicated DTO
    }

    /// <summary>
    /// Processes input provided by the moderator using the state machine.
    /// </summary>
    public ProcessResult ProcessInstruction(Guid gameId, ModeratorResponse? input)
	{
        ArgumentNullException.ThrowIfNull(input);

		if (!_sessions.TryGetValue(gameId, out var session))
		{
			return ProcessResult.Failure(new ConfirmationInstruction(privateInstruction: "ERROR: Game not found"));
		}

        var pendingInstruction = session.PendingModeratorInstruction
            ?? throw new InvalidOperationException("Internal error: No pending instruction available.");

        EnsureResponseMatchesPendingInstruction(pendingInstruction, input);

        if (pendingInstruction is FinishedGameConfirmationInstruction)
		{
			_sessions.Remove(gameId, out _);
            return new ProcessResult(true, null); // Game over, no further instructions
		}

		var result = GameFlowManager.HandleInput(session, input);

		return result;
	}

	// --- Helper Methods ---
	#region Helpers

	private static void EnforceRolesAreSupported(IReadOnlyCollection<MainRoleType> roles)
	{
		var unsupportedRoles = SupportedRoleCatalog.GetUnsupportedRoles(roles);

		if (unsupportedRoles.Count == 0)
		{
			return;
		}

		throw new InvalidOperationException(
			$"Game session configuration contains unsupported Roles: {string.Join(", ", unsupportedRoles)}.");
	}
	
    /// <summary>
    /// Revalidates a response against the instruction currently pending at
    /// consumption time, before any session state can change.
    /// </summary>
    private static void EnsureResponseMatchesPendingInstruction(
        ModeratorInstruction pendingInstruction,
        ModeratorResponse response)
    {
        if (response.InstructionId != pendingInstruction.InstructionId)
        {
            throw new InvalidOperationException(
                "Moderator Response does not match the pending Moderator Instruction.");
        }

        if (!DoesResponseTypeMatchInstruction(pendingInstruction, response))
        {
            throw new InvalidOperationException(
                "Moderator Response type does not match the pending Moderator Instruction.");
        }

        try
        {
            RevalidateResponsePayload(pendingInstruction, response);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Moderator Response payload is not valid for the pending Moderator Instruction.",
                exception);
        }
    }

    private static void RevalidateResponsePayload(
        ModeratorInstruction instruction,
        ModeratorResponse response)
    {
        switch (instruction)
        {
            case ConfirmationInstruction:
                EnsureNoPayload(response);
                break;

            case SelectPlayersInstruction selectPlayers:
                if (response.SelectedPlayerIds is null ||
                    response.AssignedPlayerRoles is not null ||
                    response.SelectedOptionIds is not null)
                {
                    throw new ArgumentException("Player selection response payload is malformed.");
                }

                selectPlayers.CreateResponse(response.SelectedPlayerIds.ToHashSet());
                break;

            case AssignRolesInstruction assignRoles:
                if (response.AssignedPlayerRoles is null ||
                    response.SelectedPlayerIds is not null ||
                    response.SelectedOptionIds is not null)
                {
                    throw new ArgumentException("Role assignment response payload is malformed.");
                }

                assignRoles.CreateResponse(response.AssignedPlayerRoles.ToDictionary());
                break;

            case SelectOptionsInstruction selectOptions:
                if (response.SelectedOptionIds is null ||
                    response.SelectedPlayerIds is not null ||
                    response.AssignedPlayerRoles is not null)
                {
                    throw new ArgumentException("Option response payload is malformed.");
                }

                var canonicalResponse = selectOptions.CreateResponse(response.SelectedOptionIds);
                if (!response.SelectedOptionIds.SequenceEqual(
                    canonicalResponse.SelectedOptionIds!,
                    StringComparer.Ordinal))
                {
                    throw new ArgumentException(
                        "Selected option IDs must follow the instruction's semantic order.");
                }
                break;

            default:
                throw new ArgumentException(
                    $"Unsupported Moderator Instruction type: {instruction.GetType().Name}.");
        }
    }

    private static void EnsureNoPayload(ModeratorResponse response)
    {
        if (response.SelectedPlayerIds is not null ||
            response.AssignedPlayerRoles is not null ||
            response.SelectedOptionIds is not null)
        {
            throw new ArgumentException("Continue response must not carry a gameplay payload.");
        }
    }

    private static bool DoesResponseTypeMatchInstruction(ModeratorInstruction instruction, ModeratorResponse response)
    {
        return instruction switch
        {
            StartGameConfirmationInstruction => response.Type == Continue,
            FinishedGameConfirmationInstruction => response.Type == Continue,
            ConfirmationInstruction => response.Type == Continue,
            SelectPlayersInstruction => response.Type == PlayerSelection,
            AssignRolesInstruction => response.Type == AssignPlayerRoles,
            SelectOptionsInstruction => response.Type == OptionSelection,
            _ => false,
        };
    }

	#endregion
}
