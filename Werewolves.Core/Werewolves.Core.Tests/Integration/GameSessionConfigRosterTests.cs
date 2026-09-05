using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class GameSessionConfigRosterTests
{
	[Fact]
	public void Constructor_WithExplicitRoster_PreservesStableIdentitiesAndSeatingOrder()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(PlayerId(1), "Ana"),
			new(PlayerId(2), "Bruno"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(5), "Eduardo")
		];

		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		config.PlayerRoster.Should().Equal(roster);
		config.Players.Should().Equal(
			"Ana",
			"Bruno",
			"Catarina",
			"Diana",
			"Eduardo");
	}

	[Fact]
	public void Constructor_LegacyNamesProjectionCannotMutateAuthoritativeRoster()
	{
		var roster = CreateRoster();
		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var legacyNames = (IList<string>)config.Players;

		var act = () => legacyNames[0] = "Changed";

		act.Should().Throw<NotSupportedException>();
		config.PlayerRoster.Select(player => player.Name)
			.Should().Equal("Ana", "Bruno", "Catarina", "Diana", "Eduardo");
	}

	[Fact]
	public void Constructor_WithDuplicatePlayerIdentity_IsRejected()
	{
		var duplicateId = PlayerId(1);
		GameSessionPlayerConfig[] roster =
		[
			new(duplicateId, "Ana"),
			new(duplicateId, "Bruno"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(5), "Eduardo")
		];

		var act = () => new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("playerRoster");
	}

	[Fact]
	public void StartNewGame_UsesExactConfiguredPlayerIdentitiesInSeatingOrder()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(PlayerId(5), "Eduardo"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(1), "Ana"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(2), "Bruno")
		];
		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var service = new GameService();

		var instruction = service.StartNewGame(config);
		var session = service.GetGameStateView(instruction.GameGuid);

		session.Should().NotBeNull();
		session!.GetPlayers().Select(player => (player.Id, player.Name)).Should().Equal(
			roster.Select(player => (player.Id, player.Name)));
	}

	[Fact]
	public void StartNewGame_WithLegacyPlayerNames_ReusesTheAllocatedRoster()
	{
		List<string> playerNames =
		[
			"Ana",
			"Bruno",
			"Catarina",
			"Diana",
			"Eduardo"
		];
		var config = new GameSessionConfig(
			playerNames,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var configuredRoster = config.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var service = new GameService();

		var instruction = service.StartNewGame(config);
		var session = service.GetGameStateView(instruction.GameGuid);

		configuredRoster.Select(player => player.Id)
			.Should().NotContain(Guid.Empty);
		configuredRoster.Select(player => player.Id)
			.Should().OnlyHaveUniqueItems();
		configuredRoster.Select(player => player.Name)
			.Should().Equal(playerNames);
		session.Should().NotBeNull();
		session!.GetPlayers()
			.Select(player => (player.Id, player.Name))
			.Should().Equal(configuredRoster);
	}

	[Fact]
	public void Constructor_WhenPrejudicedManipulatorIsReachableWithoutPartition_IsRejected()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(PlayerId(1), "Ana"),
			new(PlayerId(2), "Bruno"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(5), "Eduardo")
		];

		var act = () => new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		act.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Constructor_WhenPrejudicedManipulatorIsAThiefOffer_RequiresPartition(
		bool manipulatorIsOffer1)
	{
		var roster = CreateRoster();
		MainRoleType[] roleComposition =
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.PrejudicedManipulator
		];
		var offer1 = manipulatorIsOffer1
			? MainRoleType.PrejudicedManipulator
			: MainRoleType.SimpleVillager;
		var offer2 = manipulatorIsOffer1
			? MainRoleType.SimpleVillager
			: MainRoleType.PrejudicedManipulator;
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			playerCount: roster.Length,
			roleComposition,
			offer1,
			offer2);
		var partition = CreatePartition(roster);

		var withoutPartition = () => new GameSessionConfig(roster, roleLockIn);
		var withPartition = new GameSessionConfig(
			roster,
			roleLockIn,
			publicGroupPartition: partition);

		withoutPartition.Should().Throw<InvalidOperationException>();
		withPartition.PublicGroupPartition.Should().Be(partition);
	}

	[Fact]
	public void Constructor_WhenPrejudicedManipulatorIsUnreachableWithPartition_IsRejected()
	{
		var roster = CreateRoster();
		var partition = CreatePartition(roster);

		var act = () => new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("publicGroupPartition");
	}

	[Fact]
	public void StartNewGame_CarriesTheExactConfiguredPublicGroupPartition()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(PlayerId(1), "Ana"),
			new(PlayerId(2), "Bruno"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(5), "Eduardo")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[PlayerId(1), PlayerId(4)],
			[PlayerId(2), PlayerId(3), PlayerId(5)]);
		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition);
		var gameId = Guid.Parse("20000000-0000-0000-0000-000000000001");

		IGameSession session = new GameSession(
			gameId,
			new StartGameConfirmationInstruction(gameId),
			config);

		session.PublicGroupPartition.Should().Be(partition);
	}

	[Fact]
	public void SerializeAndRehydrate_PreservesExactRosterOrderAndPublicGroupPartition()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(PlayerId(5), "Eduardo"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(1), "Ana"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(2), "Bruno")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[PlayerId(5), PlayerId(1)],
			[PlayerId(3), PlayerId(4), PlayerId(2)]);
		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition);
		var gameId = Guid.Parse("20000000-0000-0000-0000-000000000002");
		GameSession session = new(
			gameId,
			new StartGameConfirmationInstruction(gameId),
			config);

		IGameSession rehydrated = new GameSession(
			session.SerializeRecoverySnapshot());

		rehydrated.GetPlayers()
			.Select(player => (player.Id, player.Name))
			.Should().Equal(roster.Select(player => (player.Id, player.Name)));
		rehydrated.PublicGroupPartition.Should().Be(partition);
	}

	[Fact]
	public void Rehydrate_WhenSeatingOrderIsNotAnExactRosterBijection_IsRejected()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(PlayerId(1), "Ana"),
			new(PlayerId(2), "Bruno"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(5), "Eduardo")
		];
		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var gameId = Guid.Parse("20000000-0000-0000-0000-000000000003");
		GameSession session = new(
			gameId,
			new StartGameConfirmationInstruction(gameId),
			config);
		var malformed = RecoveryPayloadTestDriver
			.Parse(session.SerializeRecoverySnapshot())
			.RewriteSeatingOrder(
				[PlayerId(1), PlayerId(1), PlayerId(3), PlayerId(4), PlayerId(5)])
			.Serialize();

		var act = () => new GameSession(malformed);

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void Rehydrate_WhenReachablePrejudicedManipulatorPartitionIsMissing_IsRejected()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(PlayerId(1), "Ana"),
			new(PlayerId(2), "Bruno"),
			new(PlayerId(3), "Catarina"),
			new(PlayerId(4), "Diana"),
			new(PlayerId(5), "Eduardo")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[PlayerId(1), PlayerId(2)],
			[PlayerId(3), PlayerId(4), PlayerId(5)]);
		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition);
		var gameId = Guid.Parse("20000000-0000-0000-0000-000000000004");
		GameSession session = new(
			gameId,
			new StartGameConfirmationInstruction(gameId),
			config);
		var malformed = RecoveryPayloadTestDriver
			.Parse(session.SerializeRecoverySnapshot())
			.RemovePublicGroupPartition()
			.Serialize();

		var act = () => new GameSession(malformed);

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void Rehydrate_WhenPrejudicedManipulatorIsUnreachableWithPartition_IsRejected()
	{
		var roster = CreateRoster();
		var partition = CreatePartition(roster);
		var config = new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var gameId = Guid.Parse("20000000-0000-0000-0000-000000000005");
		GameSession session = new(
			gameId,
			new StartGameConfirmationInstruction(gameId),
			config);
		var malformed = RecoveryPayloadTestDriver
			.Parse(session.SerializeRecoverySnapshot())
			.RewritePublicGroupPartition(
				partition.FirstGroupPlayerIds,
				partition.SecondGroupPlayerIds)
			.Serialize();

		var act = () => new GameSession(malformed);

		act.Should().Throw<InvalidOperationException>();
	}

	private static GameSessionPlayerConfig[] CreateRoster() =>
	[
		new(PlayerId(1), "Ana"),
		new(PlayerId(2), "Bruno"),
		new(PlayerId(3), "Catarina"),
		new(PlayerId(4), "Diana"),
		new(PlayerId(5), "Eduardo")
	];

	private static PublicGroupPartition CreatePartition(
		IReadOnlyCollection<GameSessionPlayerConfig> roster) =>
		PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[PlayerId(1), PlayerId(4)],
			[PlayerId(2), PlayerId(3), PlayerId(5)]);

	private static Guid PlayerId(int value) =>
		Guid.Parse($"10000000-0000-0000-0000-{value:D12}");
}
