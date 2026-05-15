using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class GameClientManagerTests
{
	[Fact]
	public void StartGame_FromLobbyConfiguration_CreatesCoreSessionAndExposesInstruction()
	{
		var manager = new GameClientManager();
		var players = new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" };
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};

		var instruction = manager.StartGame(players, roles);

		instruction.Should().BeOfType<StartGameConfirmationInstruction>();
		manager.HasActiveSession.Should().BeTrue();
		manager.ActiveGameId.Should().Be(instruction.GameGuid);
		manager.CurrentInstruction.Should().Be(instruction);
		manager.CurrentSession.Should().NotBeNull();
		manager.CurrentSession!.GetPlayers().Select(p => p.Name).Should().Equal(players);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.Seer).Should().Be(1);
		manager.CurrentSession.RoleInPlayCount(MainRoleType.SimpleVillager).Should().Be(3);
	}

	[Fact]
	public void ProcessInput_ForCurrentInstruction_AdvancesCurrentInstruction()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);

		var result = manager.ProcessInput(startInstruction.CreateResponse(true));

		result.IsSuccess.Should().BeTrue();
		result.ModeratorInstruction.Should().NotBeNull();
		manager.CurrentInstruction.Should().Be(result.ModeratorInstruction);
		manager.CurrentInstruction.Should().NotBe(startInstruction);
		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.TurnNumber.Should().Be(1);
	}

	[Fact]
	public void ProcessInput_SuccessfulInput_WritesSingleSaveFileWithSerializedSession()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var manager = new GameClientManager(new GameService(), saveStore);
		var startInstruction = StartSimpleGame(manager);

		manager.ProcessInput(startInstruction.CreateResponse(true));

		var saveFiles = Directory.GetFiles(saveDirectory.Path);
		saveFiles.Should().ContainSingle();
		File.ReadAllText(saveFiles.Single()).Should().Be(manager.CurrentSession!.Serialize());
	}

	[Fact]
	public void ProcessInput_SuccessfulInput_OverwritesExistingSaveFile()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var manager = new GameClientManager(new GameService(), saveStore);
		var startInstruction = StartSimpleGame(manager);
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		File.WriteAllText(saveFilePath, "stale save data");

		manager.ProcessInput(startInstruction.CreateResponse(true));

		Directory.GetFiles(saveDirectory.Path).Should().ContainSingle();
		File.ReadAllText(saveFilePath).Should().Be(manager.CurrentSession!.Serialize());
	}

	[Fact]
	public void Constructor_WhenSaveFileExists_RehydratesSessionAndCurrentInstruction()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveStore = new FileGameSessionSaveStore(saveDirectory.Path);
		var manager = new GameClientManager(new GameService(), saveStore);
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse(true));
		var savedGameId = manager.ActiveGameId;
		var savedPhase = manager.CurrentPhase;

		var resumed = new GameClientManager(new GameService(), new FileGameSessionSaveStore(saveDirectory.Path));

		resumed.HasActiveSession.Should().BeTrue();
		resumed.ActiveGameId.Should().Be(savedGameId);
		resumed.CurrentSession.Should().NotBeNull();
		resumed.CurrentInstruction.Should().NotBeNull();
		resumed.CurrentPhase.Should().Be(savedPhase);
	}

	[Fact]
	public void ProcessInput_AfterResume_ContinuesFromRestoredInstruction()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(new GameService(), new FileGameSessionSaveStore(saveDirectory.Path));
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse(true));
		var resumed = new GameClientManager(new GameService(), new FileGameSessionSaveStore(saveDirectory.Path));
		var restoredInstruction = resumed.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;

		var result = resumed.ProcessInput(restoredInstruction.CreateResponse(true));

		result.IsSuccess.Should().BeTrue();
		resumed.CurrentInstruction.Should().Be(result.ModeratorInstruction);
		resumed.CurrentInstruction.Should().NotBe(restoredInstruction);
		resumed.HasActiveSession.Should().BeTrue();
	}

	[Fact]
	public void Constructor_WhenSaveFileIsCorrupted_DoesNotThrowAndClearsSave()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		File.WriteAllText(saveFilePath, "not valid session json");

		var act = () => new GameClientManager(new GameService(), new FileGameSessionSaveStore(saveDirectory.Path));

		var manager = act.Should().NotThrow().Subject;
		manager.HasActiveSession.Should().BeFalse();
		File.Exists(saveFilePath).Should().BeFalse();
	}

	[Fact]
	public void StartGame_WhenSaveFileExists_DeletesSaveFile()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		var manager = new GameClientManager(new GameService(), new FileGameSessionSaveStore(saveDirectory.Path));
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse(true));
		File.Exists(saveFilePath).Should().BeTrue();

		StartSimpleGame(manager);

		File.Exists(saveFilePath).Should().BeFalse();
	}

	[Fact]
	public void ProcessInput_WhenVictoryInstructionIsReached_DeletesSaveFile()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var saveFilePath = Path.Combine(saveDirectory.Path, FileGameSessionSaveStore.SaveFileName);
		var manager = new GameClientManager(new GameService(), new FileGameSessionSaveStore(saveDirectory.Path));

		PlayToWerewolfVictoryAtDawn(manager);

		manager.CurrentInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();
		File.Exists(saveFilePath).Should().BeFalse();
	}

	[Fact]
	public void ProcessInput_WhenSaveFails_DoesNotThrowAndKeepsGameProgress()
	{
		var manager = new GameClientManager(new GameService(), new ThrowingSaveStore());
		var startInstruction = StartSimpleGame(manager);

		var act = () => manager.ProcessInput(startInstruction.CreateResponse(true));

		act.Should().NotThrow();
		manager.HasActiveSession.Should().BeTrue();
		manager.CurrentInstruction.Should().NotBe(startInstruction);
	}

	[Fact]
	public void DisplayFlow_ForInstructionWithPublicAndPrivateText_ShowsAnnouncementBeforeInput()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse(true));
		var instruction = manager.CurrentInstruction!;

		instruction.PublicAnnouncement.Should().NotBeNullOrWhiteSpace();
		instruction.PrivateInstruction.Should().NotBeNullOrWhiteSpace();

		var flow = new InstructionDisplayFlow(instruction);

		flow.CurrentText.Should().Be(instruction.PublicAnnouncement);
		flow.IsShowingInput.Should().BeFalse();

		flow.Advance();

		flow.CurrentText.Should().Be(instruction.PrivateInstruction);
		flow.IsShowingInput.Should().BeTrue();
	}

	[Fact]
	public void DisplayFlow_ForSinglePartInstruction_ShowsTextAndInputImmediately()
	{
		var manager = new GameClientManager();
		var instruction = StartSimpleGame(manager);

		var flow = new InstructionDisplayFlow(instruction);

		flow.CurrentText.Should().Be(instruction.PublicAnnouncement);
		flow.IsShowingInput.Should().BeTrue();
	}

	[Fact]
	public void ProcessInput_WithConfirmationOnlyInstructions_AdvancesPhaseAndKeepsVisibleTextPortuguese()
	{
		var manager = new GameClientManager();
		var startInstruction = StartVillagerOnlyGame(manager);

		manager.ProcessInput(startInstruction.CreateResponse(true));
		var nightStartInstruction = manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		nightStartInstruction.PublicAnnouncement.Should().Be("A aldeia adormece.");
		nightStartInstruction.PrivateInstruction.Should().Be("Confirma que todos estão a dormir e em silêncio.");
		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.TurnNumber.Should().Be(1);

		manager.ProcessInput(nightStartInstruction.CreateResponse(true));
		var dawnInstruction = manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		dawnInstruction.PublicAnnouncement.Should().Be("As ações da noite terminaram. A aldeia acorda.");
		manager.CurrentPhase.Should().Be(GamePhase.Dawn);
		manager.TurnNumber.Should().Be(1);

		manager.ProcessInput(dawnInstruction.CreateResponse(true));

		manager.CurrentInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();
		manager.CurrentPhase.Should().Be(GamePhase.Day);
		manager.TurnNumber.Should().Be(1);
	}

	[Fact]
	public void StartGame_RaisesStateChangedOnceAfterSessionCreation()
	{
		var manager = new GameClientManager();
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		StartSimpleGame(manager);

		eventCount.Should().Be(1);
	}

	[Fact]
	public void ProcessInput_RaisesStateChangedAfterSuccessfulProcessing()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		manager.ProcessInput(startInstruction.CreateResponse(true));

		eventCount.Should().Be(1);
	}

	[Fact]
	public void CurrentRoster_UpdatesWhenRoleInformationChangesDuringNight()
	{
		var manager = new GameClientManager();
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse(true));
		var nightStartInstruction = manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(nightStartInstruction.CreateResponse(true));
		var identifyWerewolfInstruction = manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var werewolf = manager.CurrentSession!.GetPlayers().First();
		var eventRosterSnapshots = new List<IReadOnlyList<DashboardRosterEntry>>();
		manager.StateChanged += (_, _) => eventRosterSnapshots.Add(manager.CurrentRoster);

		manager.ProcessInput(identifyWerewolfInstruction.CreateResponse([werewolf.Id]));

		manager.CurrentRoster[0].RoleLabel.Should().Be("Lobisomem");
		manager.CurrentRoster[0].IsRoleKnown.Should().BeTrue();
		eventRosterSnapshots.Should().ContainSingle();
		eventRosterSnapshots[0][0].RoleLabel.Should().Be("Lobisomem");
	}

	[Fact]
	public void ProcessInput_WithoutActiveSession_ThrowsInvalidOperationException()
	{
		var manager = new GameClientManager();
		var response = StartSimpleGame(new GameClientManager()).CreateResponse(true);

		var act = () => manager.ProcessInput(response);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Cannot process moderator response without an active game session.");
	}

	[Fact]
	public async Task StartGame_ReconcilesAudioForDisplayedInstruction()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);

		var instruction = StartSimpleGame(manager);
		await manager.PendingAudioReconciliation;

		audioPlayback.ReconciledInstructions.Should().Equal(instruction);
	}

	[Fact]
	public async Task ProcessInput_ReconcilesAudioForNewInstruction()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);
		var startInstruction = StartSimpleGame(manager);
		await manager.PendingAudioReconciliation;
		audioPlayback.ReconciledInstructions.Clear();

		var result = manager.ProcessInput(startInstruction.CreateResponse(true));
		await manager.PendingAudioReconciliation;

		audioPlayback.ReconciledInstructions.Should().Equal(result.ModeratorInstruction);
	}

	[Fact]
	public async Task ToggleAudioMuteAsync_UpdatesMuteStateAndRaisesStateChanged()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);
		StartSimpleGame(manager);
		var eventCount = 0;
		manager.StateChanged += (_, _) => eventCount++;

		await manager.ToggleAudioMuteAsync();

		manager.IsAudioMuted.Should().BeTrue();
		audioPlayback.MuteRequests.Should().Equal(true);
		eventCount.Should().Be(1);
	}

	[Fact]
	public async Task ReconcileAudioAfterResumeAsync_ReconcilesCurrentInstruction()
	{
		var audioPlayback = new FakeInstructionAudioPlayback();
		var manager = new GameClientManager(new GameService(), audioPlayback);
		StartSimpleGame(manager);
		await manager.PendingAudioReconciliation;
		audioPlayback.ReconciledInstructions.Clear();

		await manager.ReconcileAudioAfterResumeAsync();

		audioPlayback.ReconciledInstructions.Should().Equal(manager.CurrentInstruction);
	}

	[Fact]
	public void DebateElapsed_BeforeDebatePhase_ReturnsNull()
	{
		var manager = new GameClientManager(new GameService(), timeProvider: new FakeTimeProvider(DateTimeOffset.UtcNow));
		StartSimpleGame(manager);

		manager.DebateElapsed.Should().BeNull();
	}

	[Fact]
	public void DebateElapsed_DuringDebateInstruction_ReturnsElapsedTime()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);

		fakeTime.Advance(TimeSpan.FromSeconds(42));

		manager.DebateElapsed.Should().NotBeNull();
		manager.DebateElapsed!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(42), TimeSpan.FromMilliseconds(50));
	}

	[Fact]
	public void DebateElapsed_DuringNightPhase_ReturnsNull()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse(true));

		manager.CurrentPhase.Should().Be(GamePhase.Night);
		manager.DebateElapsed.Should().BeNull();
	}

	[Fact]
	public void DebateElapsed_AfterVotingBegins_ReturnsNull()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);
		manager.DebateElapsed.Should().NotBeNull();

		// Confirm the debate instruction to advance to voting
		var debateInstruction = (ConfirmationInstruction)manager.CurrentInstruction!;
		manager.ProcessInput(debateInstruction.CreateResponse(true));

		manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>();
		manager.DebateElapsed.Should().BeNull();
	}

	[Fact]
	public void DebateElapsed_WhenNewDebateBegins_ResetsTimer()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);
		fakeTime.Advance(TimeSpan.FromMinutes(5));
		var firstDebateElapsed = manager.DebateElapsed!.Value;
		firstDebateElapsed.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(50));

		// Move past debate through voting and back to a second debate
		AdvancePastDebateToNextDebate(manager);

		// Timer should have reset -- elapsed should be near zero, not 5+ minutes
		manager.DebateElapsed.Should().NotBeNull();
		manager.DebateElapsed!.Value.Should().BeLessThan(TimeSpan.FromSeconds(1));
	}

	private static StartGameConfirmationInstruction StartSimpleGame(GameClientManager manager)
	{
		var players = new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" };
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};

		return manager.StartGame(players, roles);
	}

	private sealed class FakeInstructionAudioPlayback : IInstructionAudioPlayback
	{
		public bool IsMuted { get; private set; }
		public List<ModeratorInstruction?> ReconciledInstructions { get; } = [];
		public List<bool> MuteRequests { get; } = [];

		public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken cancellationToken = default)
		{
			ReconciledInstructions.Add(instruction);
			return Task.CompletedTask;
		}

		public Task SetMutedAsync(
			bool isMuted,
			ModeratorInstruction? instruction,
			CancellationToken cancellationToken = default)
		{
			IsMuted = isMuted;
			MuteRequests.Add(isMuted);
			return ReconcileAsync(instruction, cancellationToken);
		}
	}

	private static void PlayToWerewolfVictoryAtDawn(GameClientManager manager)
	{
		var startInstruction = manager.StartGame(
			["Ana", "Bruno", "Catarina", "Diana", "Eduardo"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		manager.ProcessInput(startInstruction.CreateResponse(true));
		var players = manager.CurrentSession!.GetPlayers().ToList();
		var werewolfIds = players.Take(2).Select(player => player.Id).ToHashSet();
		var victimId = players[2].Id;

		ConfirmCurrentInstruction(manager);
		SelectCurrentPlayers(manager, werewolfIds);
		SelectCurrentPlayers(manager, [victimId]);
		ConfirmCurrentInstruction(manager);
		ConfirmCurrentInstruction(manager);

		for (var step = 0; step < 20; step++)
		{
			switch (manager.CurrentInstruction)
			{
				case FinishedGameConfirmationInstruction:
					return;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					manager.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				case ConfirmationInstruction confirmation:
					manager.ProcessInput(confirmation.CreateResponse(true));
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected instruction while reaching victory: {manager.CurrentInstruction?.GetType().Name ?? "null"}.");
			}
		}

		throw new InvalidOperationException("Victory was not reached within the expected number of inputs.");
	}

	private static void ConfirmCurrentInstruction(GameClientManager manager)
	{
		var instruction = manager.CurrentInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		manager.ProcessInput(instruction.CreateResponse(true));
	}

	private static void SelectCurrentPlayers(GameClientManager manager, HashSet<Guid> playerIds)
	{
		var instruction = manager.CurrentInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		manager.ProcessInput(instruction.CreateResponse(playerIds));
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		private TemporaryDirectory(string path)
		{
			Path = path;
		}

		public string Path { get; }

		public static TemporaryDirectory Create() =>
			new(Directory.CreateTempSubdirectory("werewolves-client-tests-").FullName);

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
	}

	[Fact]
	public void DebateElapsed_ContinuesGrowingWithoutInteraction()
	{
		var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var manager = new GameClientManager(new GameService(), timeProvider: fakeTime);
		AdvanceToDebate(manager);

		fakeTime.Advance(TimeSpan.FromSeconds(10));
		var first = manager.DebateElapsed!.Value;

		fakeTime.Advance(TimeSpan.FromSeconds(20));
		var second = manager.DebateElapsed!.Value;

		first.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));
		second.Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50));
	}

	private static void AdvancePastDebateToNextDebate(GameClientManager manager)
	{
		// Confirm debate to move to voting
		var debateInstruction = (ConfirmationInstruction)manager.CurrentInstruction!;
		manager.ProcessInput(debateInstruction.CreateResponse(true));

		// Continue through voting, night, dawn until the next debate
		for (var step = 0; step < 50; step++)
		{
			if (manager.CurrentPhase == GamePhase.Day &&
				manager.CurrentInstruction is ConfirmationInstruction &&
				manager.CurrentInstruction.PublicAnnouncement == GameStrings.DebateStartsPrompt)
			{
				return;
			}

			switch (manager.CurrentInstruction)
			{
				case FinishedGameConfirmationInstruction:
					throw new InvalidOperationException("Game ended before reaching next debate.");
				case ConfirmationInstruction ci:
					manager.ProcessInput(ci.CreateResponse(true));
					break;
				case SelectPlayersInstruction sp:
					// Vote for nobody (empty set if optional) to avoid eliminations
					if (sp.CountConstraint.IsOptional)
					{
						manager.ProcessInput(sp.CreateResponse([]));
					}
					else
					{
						var firstId = sp.SelectablePlayerIds.First();
						manager.ProcessInput(sp.CreateResponse([firstId]));
					}
					break;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					manager.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected instruction: {manager.CurrentInstruction?.GetType().Name ?? "null"}");
			}
		}

		throw new InvalidOperationException("Next debate instruction was not reached within the expected number of inputs.");
	}

	private static void AdvanceToDebate(GameClientManager manager)
	{
		var startInstruction = StartSimpleGame(manager);
		manager.ProcessInput(startInstruction.CreateResponse(true));

		for (var step = 0; step < 50; step++)
		{
			if (manager.CurrentPhase == GamePhase.Day &&
				manager.CurrentInstruction is ConfirmationInstruction &&
				manager.CurrentInstruction.PublicAnnouncement == GameStrings.DebateStartsPrompt)
			{
				return;
			}

			switch (manager.CurrentInstruction)
			{
				case ConfirmationInstruction ci:
					manager.ProcessInput(ci.CreateResponse(true));
					break;
				case SelectPlayersInstruction sp:
					var firstId = sp.SelectablePlayerIds.First();
					manager.ProcessInput(sp.CreateResponse([firstId]));
					break;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					manager.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected instruction while advancing to debate: {manager.CurrentInstruction?.GetType().Name ?? "null"}");
			}
		}

		throw new InvalidOperationException("Debate instruction was not reached within the expected number of inputs.");
	}

	private sealed class FakeTimeProvider : TimeProvider
	{
		private DateTimeOffset _utcNow;

		public FakeTimeProvider(DateTimeOffset startTime)
		{
			_utcNow = startTime;
		}

		public override DateTimeOffset GetUtcNow() => _utcNow;

		public void Advance(TimeSpan delta)
		{
			_utcNow += delta;
		}
	}

	private sealed class ThrowingSaveStore : IGameSessionSaveStore
	{
		public string? Load() => null;

		public void Save(string serializedSession) =>
			throw new IOException("Save failed.");

		public void Clear()
		{
		}
	}

	private static StartGameConfirmationInstruction StartVillagerOnlyGame(GameClientManager manager)
	{
		var players = new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" };
		var roles = Enumerable.Repeat(MainRoleType.SimpleVillager, players.Length).ToArray();

		return manager.StartGame(players, roles);
	}
}
