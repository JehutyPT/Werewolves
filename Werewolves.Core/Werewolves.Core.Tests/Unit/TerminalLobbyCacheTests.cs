using FluentAssertions;
using System.Text;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class TerminalLobbyCacheTests
{
	[Fact]
	public void Capture_AlreadyDecided_PreservesCompleteTerminalMeaningWithoutEvidence()
	{
		var identity = Identity();
		var result = new AlreadyDecidedTerminalEvaluation(
			new SharedVictoryGameResult([Faction.Werewolf, Faction.Villager]),
			AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied);

		var record = TerminalLobbyCache.Capture(identity, result);

		var decided = record.Should().BeOfType<AlreadyDecidedTerminalCacheRecord>().Subject;
		decided.CompatibilityIdentity.Should().Be(identity);
		decided.GameResult.Should().Be(result.GameResult);
		decided.Reason.Should().Be(result.Reason);
	}

	[Fact]
	public void WriteAndRead_AlreadyDecided_UsesStrictCanonicalGoldenBytes()
	{
		var identity = Identity();
		var record = new AlreadyDecidedTerminalCacheRecord(identity,
			new SingleFactionGameResult(Faction.Villager),
			AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit);
		const string golden = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{\"identity\":\"profile=baseline-random@1|players=5|roles=[SimpleVillager=4,SimpleWerewolf=1]|actor=[]|rules=[]\",\"kind\":\"alreadyDecided\",\"result\":{\"kind\":0,\"factions\":[0]},\"reason\":1}}";

		TerminalLobbyCache.Write(record).Should().Equal(Encoding.UTF8.GetBytes(golden));
		var read = TerminalLobbyCache.Read(Encoding.UTF8.GetBytes(golden), identity);
		read.IsUsable.Should().BeTrue();
		read.Record.Should().BeEquivalentTo(record);
	}

	[Theory]
	[InlineData("{\"schema\":\"stale\",\"version\":1,\"record\":{}}")]
	[InlineData("{\"schema\":\"terminal-lobby-cache\",\"version\":2,\"record\":{}}")]
	[InlineData("{\"schema\":\"terminal-lobby-cache\",\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{}}")]
	[InlineData("{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"extra\":0,\"record\":{}}")]
	public void Read_MalformedOrAmbiguousPayload_IsUnusableAsAWhole(string json)
	{
		TerminalLobbyCache.Read(Encoding.UTF8.GetBytes(json), Identity()).IsUsable.Should().BeFalse();
	}

	[Fact]
	public void AggregateRecords_RequireCompleteExactPartitionAndPolicyDenominators()
	{
		var identity = Identity();
		var villager = new SingleFactionGameResult(Faction.Villager);
		var wolf = new SingleFactionGameResult(Faction.Werewolf);
		var rows = new[] { new TerminalCacheGameResultFrequency(villager, 750, 1000), new TerminalCacheGameResultFrequency(wolf, 250, 1000) };
		var cells = new[] { new TerminalCacheTurnWindowFrequency(villager, 1, VictoryCheckWindow.Dawn, 750, 1000), new TerminalCacheTurnWindowFrequency(wolf, 1, VictoryCheckWindow.PreNight, 250, 1000) };

		var record = new DegenerateTerminalCacheRecord(identity, rows, cells);
		record.GameResultFrequencies.Should().Equal(rows);
		Action missingPartition = () => new DegenerateTerminalCacheRecord(identity, rows, cells[..1]);
		missingPartition.Should().Throw<ArgumentException>();
		Action lateEnding = () => new DegenerateTerminalCacheRecord(identity, rows,
			[new TerminalCacheTurnWindowFrequency(villager, 2, VictoryCheckWindow.Dawn, 750, 1000), cells[1]]);
		lateEnding.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Document_SortsByFullIdentityRejectsDuplicatesAndSupportsExactCompatibilityLookup()
	{
		var firstIdentity = Identity();
		var secondIdentity = new SimulationCompatibilityIdentity(firstIdentity.Scenario, new SimulatorProfileIdentity("baseline-random", "2"));
		var first = new AlreadyDecidedTerminalCacheRecord(firstIdentity, new NoWinnerGameResult(), AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied);
		var second = new AlreadyDecidedTerminalCacheRecord(secondIdentity, new SingleFactionGameResult(Faction.Werewolf), AlreadyDecidedReason.WerewolfControlShortcut);

		var document = TerminalLobbyCache.CreateDocument([second, first]);
		document.Records.Should().Equal(first, second);
		TerminalLobbyCache.TryGet(document, secondIdentity, out var found).Should().BeTrue();
		found.Should().BeSameAs(second);
		Action duplicate = () => TerminalLobbyCache.CreateDocument([first, first]);
		duplicate.Should().Throw<ArgumentException>();
		var bytes = TerminalLobbyCache.Write(document);
		TerminalLobbyCache.ReadDocument(bytes, [firstIdentity, secondIdentity]).IsUsable.Should().BeTrue();
		TerminalLobbyCache.ReadDocument(bytes, [firstIdentity]).IsUsable.Should().BeFalse();
	}

	[Theory]
	[InlineData(1000, true)]
	[InlineData(10000, false)]
	public void Capture_AggregateTerminalVariants_ProjectsExactCompactValues(int count, bool degenerate)
	{
		var identity = Identity();
		var villager = new SingleFactionGameResult(Faction.Villager);
		var wolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var runs = Enumerable.Range(0, count).Select(i => new CompletedSimulationRun(
			new RunSeedMaterial(identity, BaselineRandomDecisionStrategy.Identity, i),
			i < count / 2 ? villager : wolf,
			degenerate ? 1 : (i % 2) + 1,
			i % 2 == 0 ? VictoryCheckWindow.Dawn : VictoryCheckWindow.PreNight));
		var source = new SimulationBatchSourceEvidence(identity.Scenario, identity.Profile,
			BaselineRandomDecisionStrategy.Identity, runs);
		var evidence = new SimulationResultEvidence(source, [Faction.Villager, Faction.Werewolf],
			[villager, wolf, noWinner]);

		var record = TerminalLobbyCache.Capture(identity, degenerate
			? new DegenerateTerminalEvaluation(evidence)
			: new ProbabilityTerminalEvaluation(evidence));

		var aggregate = record.Should().BeAssignableTo<AggregateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(count);
		aggregate.GameResultFrequencies.Should().ContainSingle(x => x.GameResult.Equals(noWinner) && x.Numerator == 0);
		aggregate.GameResultFrequencyByTurn.Sum(x => x.Numerator).Should().Be(count);
		TerminalLobbyCache.Read(TerminalLobbyCache.Write(record), identity).IsUsable.Should().BeTrue();
	}

	private static SimulationCompatibilityIdentity Identity() => new(
		CanonicalSimulationScenario.Parse(
			"players=5|roles=[SimpleVillager=4,SimpleWerewolf=1]|actor=[]|rules=[]"),
		new SimulatorProfileIdentity("baseline-random", "1"));
}
