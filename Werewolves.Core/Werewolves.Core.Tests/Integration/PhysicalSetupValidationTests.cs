using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public class PhysicalSetupValidationTests : DiagnosticTestBase
{
	public PhysicalSetupValidationTests(ITestOutputHelper output) : base(output)
	{
	}

	public static IEnumerable<object[]> SupportedPlayerCounts =>
		Enumerable.Range(5, 26).Select(count => new object[] { count });

	[Theory]
	[MemberData(nameof(SupportedPlayerCounts))]
	public void TryGetPlayerConfigIssues_WithSupportedPlayerCount_ReturnsNoIssues(int playerCount)
	{
		var hasIssues = GameSessionConfig.TryGetPlayerConfigIssues(
			CreatePlayerNames(playerCount),
			out var issues);

		hasIssues.Should().BeFalse();
		issues.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetPlayerConfigIssues_With31Players_ReturnsTooManyPlayers()
	{
		var players = Enumerable.Range(1, 31)
			.Select(index => $"Player {index}")
			.ToList();

		var hasIssues = GameSessionConfig.TryGetPlayerConfigIssues(players, out var issues);

		hasIssues.Should().BeTrue();
		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.TooManyPlayers);
		MarkTestCompleted();
	}

	[Fact]
	public void GetExpectedRoleCount_WithActor_ReturnsPlayerCount()
	{
		var expectedRoleCount = GameSessionConfig.GetExpectedRoleCount(
			5,
			[MainRoleType.Actor]);

		expectedRoleCount.Should().Be(5);
		MarkTestCompleted();
	}

	[Fact]
	public void Constructor_WithActorSetupCards_KeepsThemOutsideRoleComposition()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.Actor,
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.Hunter
		};
		var actorSetupCards = new ActorSetupCards(
			[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder]);

		var config = new GameSessionConfig(
			CreatePlayerNames(5),
			roleComposition,
			actorSetupCards);

		config.Roles.Should().Equal(roleComposition);
		config.ActorSetupCards.Cards.Should().Equal(actorSetupCards.Cards);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	[InlineData(4)]
	public void TryGetConfigIssues_WithActorAndNotExactlyThreeSetupCards_ReturnsCountMismatch(
		int actorSetupCardCount)
	{
		var roleComposition = CreateActorRoleComposition();
		var availableSetupCards = new[]
		{
			MainRoleType.Cupid,
			MainRoleType.Defender,
			MainRoleType.Elder,
			MainRoleType.Scapegoat
		};
		var actorSetupCards = new ActorSetupCards(
			availableSetupCards.Take(actorSetupCardCount));

		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			actorSetupCards,
			out var issues);

		hasIssues.Should().BeTrue();
		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.ActorSetupCardCountMismatch);
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithActorSetupCardInRoleComposition_ReturnsOverlapFailure()
	{
		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			CreateActorRoleComposition(),
			new ActorSetupCards(
				[MainRoleType.Seer, MainRoleType.Defender, MainRoleType.Elder]),
			out var issues);

		hasIssues.Should().BeTrue();
		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.ActorSetupCardInRoleComposition);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(MainRoleType.SimpleVillager)]
	[InlineData(MainRoleType.VillagerVillager)]
	[InlineData(MainRoleType.TwoSisters)]
	[InlineData(MainRoleType.ThreeBrothers)]
	[InlineData(MainRoleType.WhiteWerewolf)]
	[InlineData(MainRoleType.Thief)]
	[InlineData(MainRoleType.Piper)]
	public void TryGetConfigIssues_WithIneligibleActorSetupCard_ReturnsEligibilityFailure(
		MainRoleType ineligibleRole)
	{
		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			CreateActorRoleComposition(),
			new ActorSetupCards(
				[ineligibleRole, MainRoleType.Defender, MainRoleType.Elder]),
			out var issues);

		hasIssues.Should().BeTrue();
		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.IneligibleActorSetupCard);
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithoutHardAlignedWerewolf_ReturnsCoverageFailure()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.Seer,
			MainRoleType.Cupid,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.WhiteWerewolf
		};

		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			out var issues);

		hasIssues.Should().BeTrue();
		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.MissingHardAlignedWerewolf);
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithoutHardAlignedVillager_ReturnsCoverageFailure()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.BigBadWolf,
			MainRoleType.AccursedWolfFather,
			MainRoleType.WhiteWerewolf
		};

		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			out var issues);

		hasIssues.Should().BeTrue();
		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.MissingHardAlignedVillager);
		MarkTestCompleted();
	}

	[Fact]
	public void GetRoleGroup_ForActor_ReturnsVillagers()
	{
		var group = MainRoleType.Actor.GetRoleGroup();

		group.Should().Be(RoleGroup.Villagers);
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithActorAsOnlyHardAlignedVillager_ReturnsNoIssues()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf
		};

		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			new ActorSetupCards(
				[MainRoleType.Seer, MainRoleType.Witch, MainRoleType.Hunter]),
			out var issues);

		hasIssues.Should().BeFalse();
		issues.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithActorAndShortRoleComposition_ReturnsTooFewRoles()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.Actor,
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Witch
		};

		GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder]),
			out var issues);

		issues.Select(issue => issue.Type).Should().Equal(
			GameConfigValidationErrorType.TooFewRoles);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(MainRoleType.SimpleVillager)]
	[InlineData(MainRoleType.SimpleWerewolf)]
	public void GetLobbySetupMetadata_ForSimpleRole_AllowsZeroSelectedCards(
		MainRoleType simpleRole)
	{
		var metadata = new GameService().GetLobbySetupMetadata();
		var countConstraint = metadata.AvailableRoles
			.Single(role => role.Role == simpleRole)
			.CountConstraint;

		countConstraint.IsValid(Array.Empty<MainRoleType>()).Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithoutSimpleRoles_ReturnsNoIssues()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Cupid,
			MainRoleType.Witch,
			MainRoleType.Hunter
		};

		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			out var issues);

		hasIssues.Should().BeFalse();
		issues.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithDuplicateSingleRole_PreservesCardinalityFailure()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.Hunter
		};

		GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			out var issues);

		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.RoleCountMismatch);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(MainRoleType.TwoSisters, 1)]
	[InlineData(MainRoleType.ThreeBrothers, 2)]
	public void TryGetConfigIssues_WithIncompleteGroupedRole_PreservesCardinalityFailure(
		MainRoleType groupedRole,
		int groupedRoleCount)
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.Hunter
		};
		roleComposition.AddRange(Enumerable.Repeat(groupedRole, groupedRoleCount));
		while (roleComposition.Count > 5)
		{
			roleComposition.Remove(MainRoleType.Hunter);
		}
		while (roleComposition.Count < 5)
		{
			roleComposition.Add(MainRoleType.SimpleVillager);
		}

		GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			out var issues);

		issues.Should().ContainSingle(issue =>
			issue.Type == GameConfigValidationErrorType.RoleCountMismatch);
		MarkTestCompleted();
	}

	[Fact]
	public void TryGetConfigIssues_WithThiefAndActor_KeepsTheirSetupCardsDistinct()
	{
		var roleComposition = new List<MainRoleType>
		{
			MainRoleType.Actor,
			MainRoleType.Thief,
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.Defender,
			MainRoleType.Elder
		};

		var hasIssues = GameSessionConfig.TryGetConfigIssues(
			CreatePlayerNames(5),
			roleComposition,
			new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Hunter, MainRoleType.Scapegoat]),
			out var issues);

		hasIssues.Should().BeFalse();
		issues.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(GameConfigValidationErrorType.TooManyPlayers)]
	[InlineData(GameConfigValidationErrorType.ActorSetupCardCountMismatch)]
	[InlineData(GameConfigValidationErrorType.ActorSetupCardInRoleComposition)]
	[InlineData(GameConfigValidationErrorType.IneligibleActorSetupCard)]
	[InlineData(GameConfigValidationErrorType.MissingHardAlignedWerewolf)]
	[InlineData(GameConfigValidationErrorType.MissingHardAlignedVillager)]
	public void GetDisplayMessage_ForPhysicalSetupFailure_UsesResourceBackedCopy(
		GameConfigValidationErrorType errorType)
	{
		const string unlocalizedFallback = "Unlocalized fallback";
		var error = new GameConfigValidationError(errorType, unlocalizedFallback);

		var displayMessage = error.GetDisplayMessage();

		displayMessage.Should().NotBeNullOrWhiteSpace();
		displayMessage.Should().NotBe(unlocalizedFallback);
		MarkTestCompleted();
	}

	private static List<MainRoleType> CreateActorRoleComposition() =>
		[
			MainRoleType.Actor,
			MainRoleType.BigBadWolf,
			MainRoleType.Seer,
			MainRoleType.Witch,
			MainRoleType.Hunter
		];

	private static List<string> CreatePlayerNames(int count) =>
		Enumerable.Range(1, count)
			.Select(index => $"Player {index}")
			.ToList();
}
