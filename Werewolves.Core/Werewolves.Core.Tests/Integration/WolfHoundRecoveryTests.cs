using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class WolfHoundRecoveryTests
{
	[Fact]
	public void AcceptedIdentification_FreshServiceRestoresExactAlignmentChoiceWithoutReopeningIdentification()
	{
		var (builder, wolfHoundId, identification) =
			CreateGameAtWolfHoundIdentification();
		var acceptedIdentification =
			identification.CreateResponse([wolfHoundId]);
		var expectedAlignment = builder.Process(acceptedIdentification)
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var serializedSession = builder.GetGameState()!.Serialize();
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(serializedSession);
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		var recoveredAlignment = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		using (new AssertionScope())
		{
			recoveredSession.GameHistoryLog
				.OfType<RoleIdentificationLogEntry>()
				.Should().ContainSingle(entry =>
					entry.Role == MainRoleType.WolfHound &&
					entry.PlayerIds.SetEquals(new[] { wolfHoundId }));
			recoveredAlignment.InstructionId.Should()
				.Be(expectedAlignment.InstructionId);
			recoveredAlignment.Semantic.Should()
				.Be(expectedAlignment.Semantic);
			recoveredAlignment.PublicAnnouncement.Should()
				.Be(expectedAlignment.PublicAnnouncement);
			recoveredAlignment.PrivateInstruction.Should()
				.Be(expectedAlignment.PrivateInstruction);
			recoveredAlignment.AffectedPlayerIds.Should()
				.Equal(expectedAlignment.AffectedPlayerIds);
			recoveredAlignment.SoundEffects.Should()
				.Equal(expectedAlignment.SoundEffects);
			recoveredAlignment.Options.Should()
				.Equal(expectedAlignment.Options);
			recoveredAlignment.SelectionRange.Should()
				.Be(expectedAlignment.SelectionRange);
		}

		var beforeReplay =
			PublicGameSessionSnapshot.Capture(freshService, recoveredGameId);
		Action replayIdentification = () =>
			freshService.ProcessInstruction(
				recoveredGameId,
				acceptedIdentification);

		replayIdentification.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());

		var continued = freshService.ProcessInstruction(
			recoveredGameId,
			recoveredAlignment.CreateResponse(
				WolfHoundAlignmentOptionIds.Villagers));

		continued.IsSuccess.Should().BeTrue();
		continued.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>()
			.Which.Semantic.Should()
			.Be(ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSession.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry => entry.Role == MainRoleType.WolfHound)
			.Should().Be(1);
	}

	[Fact]
	public void AcceptedAlignment_FreshServiceRestoresOneFactionBatchAndExactSleepWithoutReapplyingChoice()
	{
		var (builder, wolfHoundId, identification) =
			CreateGameAtWolfHoundIdentification();
		var alignment = builder.Process(
				identification.CreateResponse([wolfHoundId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var acceptedAlignment = alignment.CreateResponse(
			WolfHoundAlignmentOptionIds.Werewolves);
		var expectedSleep = builder.Process(acceptedAlignment)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var serializedSession = builder.GetGameState()!.Serialize();
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(serializedSession);
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		var recoveredSleep = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var transition = recoveredSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.ExplicitTransition)
			.Where(entry =>
				entry.Facts.Any(fact => fact.PlayerId == wolfHoundId))
			.Should().ContainSingle().Subject;

		using (new AssertionScope())
		{
			transition.Facts.Should().HaveCount(2);
			transition.Facts.Should().ContainSingle(fact =>
				fact.PlayerId == wolfHoundId &&
				fact.Type == FactionFactType.Beneficiary &&
				fact.Faction == Faction.Werewolf);
			transition.Facts.Should().ContainSingle(fact =>
				fact.PlayerId == wolfHoundId &&
				fact.Type == FactionFactType.Agent &&
				fact.Faction == Faction.Werewolf &&
				fact.AgentKnowledge ==
				FactionAgentKnowledge.KnownAgent);
			recoveredSession
				.GetFactionBeneficiaryKnowledge(wolfHoundId)
				.Should().Be(
					FactionBeneficiaryKnowledge.Known(
						Faction.Werewolf));
			recoveredSession.GetFactionAgentKnowledge(
					wolfHoundId,
					Faction.Werewolf)
				.Should().Be(FactionAgentKnowledge.KnownAgent);
			recoveredSleep.InstructionId.Should()
				.Be(expectedSleep.InstructionId);
			recoveredSleep.Semantic.Should()
				.Be(expectedSleep.Semantic);
			recoveredSleep.PublicAnnouncement.Should()
				.Be(expectedSleep.PublicAnnouncement);
			recoveredSleep.PrivateInstruction.Should()
				.Be(expectedSleep.PrivateInstruction);
			recoveredSleep.AffectedPlayerIds.Should()
				.Equal(expectedSleep.AffectedPlayerIds);
			recoveredSleep.SoundEffects.Should()
				.Equal(expectedSleep.SoundEffects);
		}

		var beforeReplay =
			PublicGameSessionSnapshot.Capture(freshService, recoveredGameId);
		Action replayAlignment = () =>
			freshService.ProcessInstruction(
				recoveredGameId,
				acceptedAlignment);

		replayAlignment.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());
		recoveredSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.ExplicitTransition)
			.Count(entry =>
				entry.Facts.Any(fact =>
					fact.PlayerId == wolfHoundId))
			.Should().Be(1);

		var continued = freshService.ProcessInstruction(
			recoveredGameId,
			recoveredSleep.CreateResponse());

		continued.IsSuccess.Should().BeTrue();
		freshService.GetCurrentInstruction(recoveredGameId)!
			.InstructionId.Should().NotBe(recoveredSleep.InstructionId);
	}

	private static (
		GameTestBuilder Builder,
		Guid WolfHoundId,
		SelectPlayersInstruction Identification)
		CreateGameAtWolfHoundIdentification()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification = builder.ConfirmNightStart()
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var wolfHoundId = builder.GetGameState()!
			.GetPlayers().First().Id;

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should()
			.Be(MainRoleType.WolfHound);
		return (builder, wolfHoundId, identification);
	}
}
