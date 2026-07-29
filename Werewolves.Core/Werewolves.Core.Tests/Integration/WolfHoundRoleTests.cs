using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class WolfHoundRoleTests : DiagnosticTestBase
{
	public WolfHoundRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownHolder_VillagerChoiceCommitsBaseFactionFactsAndPreservesWolfHoundIdentity()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var wolfHound = builder.GetGameState()!.GetPlayers().First();

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.WolfHound);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(MainRoleType.WolfHound.GetPublicName()));

		var alignment =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(identification.CreateResponse([wolfHound.Id])));

		alignment.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseWolfHoundAlignment);
		alignment.PublicAnnouncement.Should().BeNull();
		alignment.PrivateInstruction.Should().Be(
			GameStrings.WolfHoundAlignmentInstruction);
		alignment.AffectedPlayerIds.Should().Equal(wolfHound.Id);
		alignment.SelectionRange.Should().Be(NumberRangeConstraint.Single);
		alignment.Options.Select(option => (option.Id, option.Label)).Should().Equal(
			(
				WolfHoundAlignmentOptionIds.Villagers,
				GameStrings.VillagersGroupName),
			(
				WolfHoundAlignmentOptionIds.Werewolves,
				GameStrings.WerewolvesGroupName));
		var physicalCardBeforeChoice = builder.GetGameState()!
			.GetPlayerState(wolfHound.Id)
			.PhysicalCharacterCardRole;

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(alignment.CreateResponse(
					WolfHoundAlignmentOptionIds.Villagers)));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		var session = builder.GetGameState()!;
		var state = session.GetPlayerState(wolfHound.Id);
		state.CurrentRole.Should().Be(MainRoleType.WolfHound);
		state.ModeratorKnownRole.Should().Be(MainRoleType.WolfHound);
		state.PhysicalCharacterCardRole.Should().Be(physicalCardBeforeChoice);
		state.PubliclyRevealedRole.Should().BeNull();
		session.GetFactionBeneficiaryKnowledge(wolfHound.Id).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
		session.GetFactionAgentKnowledge(wolfHound.Id, Faction.Werewolf).Should().Be(
			FactionAgentKnowledge.KnownNonAgent);

		var transition = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind == FactionFactSourceKind.ExplicitTransition &&
				entry.Source.Identifier == "wolf-hound-alignment-choice")
			.Subject;
		transition.Facts.Should().HaveCount(2);
		transition.Facts.Should().OnlyContain(fact =>
			fact.PlayerId == wolfHound.Id);
		transition.Facts.Select(fact => fact.EffectiveBoundary)
			.Distinct()
			.Should().ContainSingle();
		MarkTestCompleted();
	}
}
