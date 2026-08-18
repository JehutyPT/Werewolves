using System.Text;
using System.Text.Json;
using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class TerminalLobbyCacheTests
{
	private const string AlreadyGolden =
		"{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{\"identity\":\"profile=safety-screening@30|players=5|roles=[Actor=1,SimpleVillager=1,SimpleWerewolf=3]|actor=[Cupid,Defender,Elder]|rules=[]\",\"kind\":\"alreadyDecided\",\"result\":{\"kind\":0,\"factions\":[1]},\"reason\":2}}";

	private const string DegenerateGolden =
		"{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{\"identity\":\"profile=safety-screening@30|players=5|roles=[Actor=1,SimpleVillager=3,SimpleWerewolf=1]|actor=[Cupid,Defender,Elder]|rules=[]\",\"kind\":\"degenerate\",\"attempted\":1000,\"completed\":1000,\"incomplete\":0,\"results\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"numerator\":750,\"denominator\":1000},{\"result\":{\"kind\":0,\"factions\":[1]},\"numerator\":250,\"denominator\":1000},{\"result\":{\"kind\":2,\"factions\":[]},\"numerator\":0,\"denominator\":1000}],\"cells\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"turn\":1,\"window\":0,\"numerator\":750,\"denominator\":1000},{\"result\":{\"kind\":0,\"factions\":[1]},\"turn\":1,\"window\":1,\"numerator\":250,\"denominator\":1000}],\"inclusiveEndingTurnCutoff\":1}}";

	private const string ProbabilityGolden =
		"{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{\"identity\":\"profile=full-probability@4|players=6|roles=[SimpleVillager=5,SimpleWerewolf=1]|actor=[]|rules=[]\",\"kind\":\"probability\",\"attempted\":10000,\"completed\":10000,\"incomplete\":0,\"results\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"numerator\":7000,\"denominator\":10000},{\"result\":{\"kind\":0,\"factions\":[1]},\"numerator\":3000,\"denominator\":10000},{\"result\":{\"kind\":2,\"factions\":[]},\"numerator\":0,\"denominator\":10000}],\"cells\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"turn\":1,\"window\":0,\"numerator\":7000,\"denominator\":10000},{\"result\":{\"kind\":0,\"factions\":[1]},\"turn\":2,\"window\":1,\"numerator\":3000,\"denominator\":10000}]}}";

	[Fact]
	public void Capture_AlreadyDecided_RequiresExactSafetyProducerClassifierMeaning()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.Actor,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf
			],
			new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder]));
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		identity.Should().Be(AlreadyDecidedIdentity());
		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var evaluation = new TerminalLobbyEvaluator()
			.Evaluate(
				scenario,
				SimulatorCapability.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly)
			.Should().BeOfType<AlreadyDecidedTerminalEvaluation>().Subject;

		var record = TerminalLobbyCache.Capture(
			scenario,
			SimulatorCapability.SafetyScreening,
			evaluation);

		var decided = record.Should().BeOfType<AlreadyDecidedTerminalCacheRecord>().Subject;
		decided.CompatibilityIdentity.Should().Be(identity);
		decided.GameResult.Should().Be(evaluation.GameResult);
		decided.Reason.Should().Be(evaluation.Reason);
		classification.Cacheability.Should().BeNull(
			"already-decided classification deliberately has no Cacheability result");
		Action mismatched = () => TerminalLobbyCache.Capture(
			scenario,
			SimulatorCapability.SafetyScreening,
			new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Villager),
				AlreadyDecidedReason.NoWerewolfFactionBeneficiariesAtLobbyExit));
		mismatched.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Constructors_RejectObsoleteAndUnknownProducersAndImpossibleAlreadyDecidedResults()
	{
		var obsolete = new SimulationCompatibilityIdentity(
			AlreadyDecidedIdentity().Scenario,
			new SimulatorProfileIdentity("core-simulator", "1"));

		Action obsoleteProducer = () => new AlreadyDecidedTerminalCacheRecord(
			obsolete,
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut,
			SimulatorCapability.SafetyScreening);
		Action noWinner = () => new AlreadyDecidedTerminalCacheRecord(
			AlreadyDecidedIdentity(),
			new NoWinnerGameResult(),
			AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied,
			SimulatorCapability.SafetyScreening);
		Action impossibleShared = () => new AlreadyDecidedTerminalCacheRecord(
			AlreadyDecidedIdentity(),
			new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]),
			AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied,
			SimulatorCapability.SafetyScreening);
		Action notAlreadyDecided = () => new AlreadyDecidedTerminalCacheRecord(
			AggregateIdentity(),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut,
			SimulatorCapability.SafetyScreening);
		var staleAggregateIdentity = new SimulationCompatibilityIdentity(
			AggregateIdentity().Scenario,
			new SimulatorProfileIdentity("other-simulator", "1"));
		Action staleAggregate = () => new DegenerateTerminalCacheRecord(
			staleAggregateIdentity,
			DegenerateRows(),
			DegenerateCells(),
			SimulatorCapability.SafetyScreening);

		obsoleteProducer.Should().Throw<ArgumentException>();
		noWinner.Should().Throw<ArgumentException>();
		impossibleShared.Should().Throw<ArgumentException>();
		notAlreadyDecided.Should().Throw<ArgumentException>();
		staleAggregate.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData("{\"kind\":1,\"factions\":[0,1]}", 3)]
	[InlineData("{\"kind\":2,\"factions\":[]}", 3)]
	public void Read_RejectsCurrentCapabilitySharedAndNoWinnerAlreadyDecidedPayloads(
		string resultJson,
		int reason)
	{
		var impossibleRecord = AlreadyGolden
			.Replace(
				"{\"kind\":0,\"factions\":[1]}",
				resultJson,
				StringComparison.Ordinal)
			.Replace("\"reason\":2", $"\"reason\":{reason}", StringComparison.Ordinal);

		TerminalLobbyCache.Read(
			Utf8(impossibleRecord),
			Scenario(AlreadyDecidedIdentity()),
			SimulatorCapability.SafetyScreening).IsUsable.Should().BeFalse();
	}

	[Theory]
	[InlineData("players=4|roles=[SimpleVillager=3,SimpleWerewolf=1]|actor=[]|rules=[]")]
	[InlineData("players=31|roles=[SimpleVillager=30,SimpleWerewolf=1]|actor=[]|rules=[]")]
	[InlineData("players=30|roles=[SimpleVillager=30,SimpleWerewolf=3]|actor=[]|rules=[]")]
	[InlineData("players=30|roles=[SimpleVillager=2147483646,SimpleWerewolf=1]|actor=[]|rules=[]")]
	public void Read_RejectsUnboundedCanonicalIdentityBeforeRoleCardMaterialization(
		string canonicalScenario)
	{
		var payload = DegenerateGolden.Replace(
			AggregateIdentity().Scenario.ToString(),
			canonicalScenario,
			StringComparison.Ordinal);
		var action = () => TerminalLobbyCache.Read(
			Utf8(payload),
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening);

		action.Should().NotThrow();
		action().IsUsable.Should().BeFalse();
	}

	[Theory]
	[InlineData("players=5|roles=[SimpleVillager=5]|actor=[]|rules=[]")]
	[InlineData("players=5|roles=[PrejudicedManipulator=1,SimpleVillager=3,SimpleWerewolf=1]|actor=[]|rules=[]")]
	[InlineData("players=5|roles=[SimpleVillager=3,SimpleWerewolf=1,WildChild=1]|actor=[Cupid,Defender,Elder]|rules=[]")]
	public void Read_RejectsRulesAppOrSimulatorUnsupportedCanonicalIdentity(
		string canonicalScenario)
	{
		var payload = DegenerateGolden.Replace(
			AggregateIdentity().Scenario.ToString(),
			canonicalScenario,
			StringComparison.Ordinal);

		TerminalLobbyCache.Read(
			Utf8(payload),
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening)
			.IsUsable.Should().BeFalse();
	}

	[Fact]
	public void AlreadyDecidedRecord_HasReviewedGoldenBytesAndRoundTrips()
	{
		var record = new AlreadyDecidedTerminalCacheRecord(
			AlreadyDecidedIdentity(),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut,
			SimulatorCapability.SafetyScreening);

		TerminalLobbyCache.Write(record).Should().Equal(Utf8(AlreadyGolden));
		var read = TerminalLobbyCache.Read(
			Utf8(AlreadyGolden),
			Scenario(AlreadyDecidedIdentity()),
			SimulatorCapability.SafetyScreening);
		read.IsUsable.Should().BeTrue();
		read.Record.Should().BeEquivalentTo(record);
	}

	[Fact]
	public void DegenerateRecord_HasReviewedGoldenBytesAndRoundTripsExactCompactDistribution()
	{
		var record = DegenerateRecord();

		TerminalLobbyCache.Write(record).Should().Equal(Utf8(DegenerateGolden));
		var read = TerminalLobbyCache.Read(
			Utf8(DegenerateGolden),
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening);

		read.IsUsable.Should().BeTrue();
		var aggregate = read.Record.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(1_000);
		aggregate.CompletedRunCount.Should().Be(1_000);
		aggregate.IncompleteRunCount.Should().Be(0);
		aggregate.InclusiveEndingTurnCutoff.Should().Be(1);
		aggregate.GameResultFrequencies.Select(x => x.Numerator).Should().Equal(750, 250, 0);
		aggregate.GameResultFrequencyByTurn.Select(x => x.Numerator).Should().Equal(750, 250);
	}

	[Fact]
	public void DegenerateRecord_WithPartitionBearingPrejudicedManipulatorIdentity_RoundTripsExactly()
	{
		var partition = CanonicalPublicGroupPartition.Create(
			5,
			[1, 3],
			[2, 4, 5]);
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var manipulatorVictory = new SingleFactionGameResult(
			Faction.PrejudicedManipulator);
		var record = new DegenerateTerminalCacheRecord(
			identity,
			[
				new(new SingleFactionGameResult(Faction.Villager), 0, 1_000),
				new(new SingleFactionGameResult(Faction.Werewolf), 0, 1_000),
				new(manipulatorVictory, 1_000, 1_000),
				new(new NoWinnerGameResult(), 0, 1_000)
			],
			[
				new(
					manipulatorVictory,
					1,
					VictoryCheckWindow.Dawn,
					1_000,
					1_000)
			],
			SimulatorCapability.SafetyScreening);

		var bytes = TerminalLobbyCache.Write(record);
		var read = TerminalLobbyCache.Read(
			bytes,
			scenario,
			SimulatorCapability.SafetyScreening);

		TerminalLobbyCache.SchemaVersion.Should().Be(1);
		read.IsUsable.Should().BeTrue();
		read.Record!.CompatibilityIdentity.Should().Be(identity);
		read.Record.CompatibilityIdentity.Scenario.PublicGroupPartition
			.Should().Be(partition);
		TerminalLobbyCache.Write(read.Record).Should().Equal(bytes);
	}

	[Fact]
	public void ProbabilityRecord_HasReviewedGoldenBytesAndRoundTripsExactCompactDistribution()
	{
		var record = ProbabilityRecord();

		TerminalLobbyCache.Write(record).Should().Equal(Utf8(ProbabilityGolden));
		var read = TerminalLobbyCache.Read(
			Utf8(ProbabilityGolden),
			Scenario(ProbabilityIdentity()),
			SimulatorCapability.FullProbability);

		read.IsUsable.Should().BeTrue();
		var aggregate = read.Record.Should().BeOfType<ProbabilityTerminalCacheRecord>().Subject;
		aggregate.GameResultFrequencies.Select(x => x.Numerator).Should().Equal(7_000, 3_000, 0);
		aggregate.GameResultFrequencyByTurn.Select(x => (x.EndingTurn, x.VictoryCheckWindow))
			.Should().Equal((1, VictoryCheckWindow.Dawn), (2, VictoryCheckWindow.PreNight));
	}

	[Fact]
	public void CollectionEnvelope_HasReviewedGoldenBytesAndExactSelectionMatchesSingleRecordRead()
	{
		var degenerate = DegenerateRecord();
		var probability = ProbabilityRecord();
		var document = TerminalLobbyCache.CreateDocument([probability, degenerate]);
		var expected = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ RecordJson(ProbabilityGolden) + "," + RecordJson(DegenerateGolden) + "]}";

		var documentBytes = TerminalLobbyCache.Write(document);
		documentBytes.Should().Equal(Utf8(expected));
		var parsed = TerminalLobbyCache.ReadDocument(
			documentBytes,
			SimulatorCapabilityRegistry.Production);
		var local = TerminalLobbyCache.Read(
			TerminalLobbyCache.Write(degenerate),
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening);

		parsed.IsUsable.Should().BeTrue();
		local.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			parsed.Document!,
			SimulationScenario.FromCanonical(AggregateIdentity().Scenario),
			SimulatorCapability.SafetyScreening,
			out var selectedRecord).Should().BeTrue();
		selectedRecord.Should().BeEquivalentTo(local.Record);
	}

	[Theory]
	[InlineData("missing-zero-row")]
	[InlineData("extra-row")]
	[InlineData("wrong-denominator")]
	[InlineData("wrong-row-sum")]
	[InlineData("wrong-cell-sum")]
	[InlineData("duplicate-cell")]
	[InlineData("late-degenerate-ending")]
	public void AggregateConstructors_RejectIncompleteOrInconsistentCurrentCapabilityMeaning(string mutation)
	{
		var rows = DegenerateRows().ToList();
		var cells = DegenerateCells().ToList();
		switch (mutation)
		{
			case "missing-zero-row": rows.RemoveAt(2); break;
			case "extra-row": rows.Add(new(new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]), 0, 1000)); break;
			case "wrong-denominator": rows[0] = new(rows[0].GameResult, 750, 999); break;
			case "wrong-row-sum": rows[0] = new(rows[0].GameResult, 749, 1000); break;
			case "wrong-cell-sum": cells[0] = new(cells[0].GameResult, 1, VictoryCheckWindow.Dawn, 749, 1000); break;
			case "duplicate-cell": cells.Add(cells[0]); break;
			case "late-degenerate-ending": cells[0] = new(cells[0].GameResult, 2, VictoryCheckWindow.Dawn, 750, 1000); break;
		}

		Action construct = () => new DegenerateTerminalCacheRecord(
			AggregateIdentity(),
			rows,
			cells,
			SimulatorCapability.SafetyScreening);

		construct.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void FrequencyValueConstructors_RejectNegativeOverDenominatorAndZeroCells()
	{
		var result = new SingleFactionGameResult(Faction.Villager);
		Action negative = () => new TerminalCacheGameResultFrequency(result, -1, 1000);
		Action overDenominator = () => new TerminalCacheGameResultFrequency(result, 1001, 1000);
		Action zeroCell = () => new TerminalCacheTurnWindowFrequency(
			result,
			1,
			VictoryCheckWindow.Dawn,
			0,
			1000);

		negative.Should().Throw<ArgumentOutOfRangeException>();
		overDenominator.Should().Throw<ArgumentOutOfRangeException>();
		zeroCell.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Theory]
	[MemberData(nameof(InvalidSinglePayloads))]
	public void Read_RejectsMalformedAmbiguousOrNonCanonicalSinglePayloadAtomically(string payload)
	{
		var action = () => TerminalLobbyCache.Read(
			Utf8(payload),
			Scenario(AlreadyDecidedIdentity()),
			SimulatorCapability.SafetyScreening);

		action.Should().NotThrow();
		action().IsUsable.Should().BeFalse();
	}

	public static IEnumerable<object[]> InvalidSinglePayloads()
	{
		yield return ["{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{}}"];
		yield return [AlreadyGolden.Replace("terminal-lobby-cache", "unknown-cache", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"version\":1,", "", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"version\":1", "\"version\":1,\"extra\":0", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"schema\":\"terminal-lobby-cache\"", "\"schema\":\"terminal-lobby-cache\",\"schema\":\"terminal-lobby-cache\"", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"kind\":\"alreadyDecided\",", "", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"kind\":\"alreadyDecided\"", "\"kind\":\"alreadyDecided\",\"unknown\":0", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"kind\":\"alreadyDecided\"", "\"kind\":\"alreadyDecided\",\"kind\":\"alreadyDecided\"", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace(",\"factions\":[1]", "", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"factions\":[1]", "\"factions\":[1],\"unknown\":0", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"factions\":[1]", "\"factions\":[1],\"factions\":[1]", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"kind\":0,\"factions\":[1]", "\"kind\":9,\"factions\":[1]", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"factions\":[1]", "\"factions\":[99]", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"reason\":2", "\"reason\":99", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("alreadyDecided", "unknownKind", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("players=5", "players=05", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("safety-screening@30", "safety screening@30", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("safety-screening@30", "safety-screening@29", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"schema\":\"terminal-lobby-cache\",\"version\":1", "\"version\":1,\"schema\":\"terminal-lobby-cache\"", StringComparison.Ordinal)];
	}

	[Theory]
	[MemberData(nameof(InvalidAggregatePayloads))]
	public void Read_RejectsInvalidAggregateInventoryPartitionEnumsAndOrdering(string payload)
	{
		TerminalLobbyCache.Read(
			Utf8(payload),
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening).IsUsable.Should().BeFalse();
	}

	public static IEnumerable<object[]> InvalidAggregatePayloads()
	{
		yield return [DegenerateGolden.Replace(",{\"result\":{\"kind\":2,\"factions\":[]},\"numerator\":0,\"denominator\":1000}", "", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace(
			"],\"cells\"",
			",{\"result\":{\"kind\":1,\"factions\":[0,1]},\"numerator\":0,\"denominator\":1000}],\"cells\"",
			StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace(
			"],\"cells\"",
			",{\"result\":{\"kind\":2,\"factions\":[]},\"numerator\":0,\"denominator\":1000}],\"cells\"",
			StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"denominator\":1000", "\"denominator\":999", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"numerator\":750", "\"numerator\":749", StringComparison.Ordinal)];
		yield return [ReplaceFirst(DegenerateGolden, "\"numerator\":750", "\"numerator\":-1")];
		yield return [ReplaceFirst(DegenerateGolden, "\"numerator\":750", "\"numerator\":1001")];
		yield return [ReplaceFirst(DegenerateGolden, ",\"denominator\":1000", "")];
		yield return [ReplaceFirst(DegenerateGolden, "\"denominator\":1000", "\"denominator\":1000,\"unknown\":0")];
		yield return [ReplaceFirst(DegenerateGolden, "\"numerator\":750", "\"numerator\":750,\"numerator\":750")];
		yield return [ReplaceFirst(DegenerateGolden, ",\"turn\":1", "")];
		yield return [ReplaceFirst(DegenerateGolden, "\"window\":0", "\"window\":0,\"unknown\":0")];
		yield return [ReplaceFirst(DegenerateGolden, "\"turn\":1", "\"turn\":1,\"turn\":1")];
		yield return [DegenerateGolden.Replace("\"attempted\":1000", "\"attempted\":999", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"completed\":1000", "\"completed\":999", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"incomplete\":0", "\"incomplete\":1", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"inclusiveEndingTurnCutoff\":1", "\"inclusiveEndingTurnCutoff\":2", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"results\":", "\"missingResults\":", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"cells\":", "\"missingCells\":", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"kind\":\"degenerate\"", "\"kind\":\"degenerate\",\"unknown\":0", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"attempted\":1000", "\"attempted\":1000,\"attempted\":1000", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"turn\":1,\"window\":0", "\"turn\":2,\"window\":0", StringComparison.Ordinal)];
		yield return [DegenerateGolden.Replace("\"window\":0", "\"window\":99", StringComparison.Ordinal)];
		yield return [SwapResultRows(DegenerateGolden)];
		yield return [SwapCells(DegenerateGolden)];
	}

	[Theory]
	[MemberData(nameof(InvalidProbabilityPayloads))]
	public void Read_RejectsProbabilityMissingRowsAndPartitionViolations(string payload)
	{
		TerminalLobbyCache.Read(
			Utf8(payload),
			Scenario(ProbabilityIdentity()),
			SimulatorCapability.FullProbability).IsUsable.Should().BeFalse();
	}

	public static IEnumerable<object[]> InvalidProbabilityPayloads()
	{
		yield return [ProbabilityGolden.Replace(",{\"result\":{\"kind\":2,\"factions\":[]},\"numerator\":0,\"denominator\":10000}", "", StringComparison.Ordinal)];
		yield return [ReplaceFirst(ProbabilityGolden, "\"denominator\":10000", "\"denominator\":9999")];
		yield return [ProbabilityGolden.Replace("\"numerator\":7000", "\"numerator\":6999", StringComparison.Ordinal)];
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Read_RejectsCellOnlyNumeratorMutationWithIntactResultRows(bool degenerate)
	{
		var canonical = degenerate ? DegenerateGolden : ProbabilityGolden;
		var payload = degenerate
			? canonical.Replace(
				"\"window\":0,\"numerator\":750",
				"\"window\":0,\"numerator\":749",
				StringComparison.Ordinal)
			: canonical.Replace(
				"\"window\":0,\"numerator\":7000",
				"\"window\":0,\"numerator\":6999",
				StringComparison.Ordinal);
		using var json = JsonDocument.Parse(payload);
		var rows = json.RootElement
			.GetProperty("record")
			.GetProperty("results")
			.EnumerateArray()
			.ToArray();
		var denominator = degenerate ? 1_000 : 10_000;
		var identity = degenerate ? AggregateIdentity() : ProbabilityIdentity();
		var capability = degenerate
			? SimulatorCapability.SafetyScreening
			: SimulatorCapability.FullProbability;

		rows.Sum(row => row.GetProperty("numerator").GetInt32()).Should().Be(denominator);
		TerminalLobbyCache.Read(
			Utf8(payload),
			Scenario(identity),
			capability)
			.IsUsable.Should().BeFalse();
	}

	[Theory]
	[MemberData(nameof(InvalidDocumentEnvelopes))]
	public void ReadDocument_RejectsMalformedOrAmbiguousEnvelopeAtomically(string payload)
	{
		var action = () => TerminalLobbyCache.ReadDocument(
			Utf8(payload),
			SimulatorCapabilityRegistry.Production);

		action.Should().NotThrow();
		action().IsUsable.Should().BeFalse();
	}

	public static IEnumerable<object[]> InvalidDocumentEnvelopes()
	{
		const string empty = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":[]}";
		yield return [empty.Replace("\"records\":[]", "\"records\":{}", StringComparison.Ordinal)];
		yield return [empty.Replace("\"records\":[]", "\"unknown\":0", StringComparison.Ordinal)];
		yield return [empty.Replace("\"records\":[]", "\"records\":[],\"records\":[]", StringComparison.Ordinal)];
		yield return [empty.Replace("\"schema\":\"terminal-lobby-cache\",\"version\":1", "\"version\":1,\"schema\":\"terminal-lobby-cache\"", StringComparison.Ordinal)];
	}

	[Fact]
	public void CollectionDecoder_RejectsDuplicateIdentityMalformedMemberAndNonCanonicalOrderAtomically()
	{
		var document = TerminalLobbyCache.CreateDocument([DegenerateRecord(), ProbabilityRecord()]);
		var canonical = Encoding.UTF8.GetString(TerminalLobbyCache.Write(document));
		var malformed = canonical.Replace(RecordJson(DegenerateGolden), "{}", StringComparison.Ordinal);
		var duplicate = canonical.Replace(
			ProbabilityIdentity().ToString(),
			AggregateIdentity().ToString(),
			StringComparison.Ordinal);
		var reversed = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ RecordJson(DegenerateGolden) + "," + RecordJson(ProbabilityGolden) + "]}";
		var stale = canonical.Replace(
			"safety-screening@30",
			"safety-screening@29",
			StringComparison.Ordinal);
		Action duplicateConstructor = () => TerminalLobbyCache.CreateDocument(
			[DegenerateRecord(), DegenerateRecord()]);

		duplicateConstructor.Should().Throw<ArgumentException>();
		foreach (var payload in new[] { "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":[{}]}", malformed, duplicate, reversed, stale })
		{
			var action = () => TerminalLobbyCache.ReadDocument(
				Utf8(payload),
				SimulatorCapabilityRegistry.Production);
			action.Should().NotThrow();
			action().IsUsable.Should().BeFalse();
		}
	}

	[Fact]
	public void ReadDocument_ValidatesCurrentMixedRecordsAgainstEachRecordsProducer()
	{
		var payload = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ RecordJson(ProbabilityGolden) + ","
			+ RecordJson(AlreadyGolden) + ","
			+ RecordJson(DegenerateGolden) + "]}";

		var read = TerminalLobbyCache.ReadDocument(
			Utf8(payload),
			SimulatorCapabilityRegistry.Production);

		read.Rejection.Should().BeNull();
		read.Document!.Records.Select(record => record.CompatibilityIdentity.Profile.ToString())
			.Should().Equal("full-probability@4", "safety-screening@30", "safety-screening@30");
	}

	[Fact]
	public void TryGet_MixedCurrentCapabilitiesSelectOnlyExactCapabilityForSameScenario()
	{
		var scenario = SimulationScenario.FromCanonical(
			FullProbabilityDegenerateIdentity().Scenario);
		var safetyIdentity = SimulatorCapability.SafetyScreening
			.CreateCompatibilityIdentity(scenario);
		var fullIdentity = SimulatorCapability.FullProbability
			.CreateCompatibilityIdentity(scenario);
		var safety = new DegenerateTerminalCacheRecord(
			safetyIdentity,
			DegenerateRows(),
			DegenerateCells(),
			SimulatorCapability.SafetyScreening);
		var full = new DegenerateTerminalCacheRecord(
			fullIdentity,
			DegenerateRows(),
			DegenerateCells(),
			SimulatorCapability.FullProbability);
		var document = TerminalLobbyCache.CreateDocument([full, safety]);

		TerminalLobbyCache.TryGet(
			document,
			scenario,
			SimulatorCapability.SafetyScreening,
			out var selectedSafety).Should().BeTrue();
		selectedSafety.Should().BeSameAs(safety);
		TerminalLobbyCache.TryGet(
			document,
			scenario,
			SimulatorCapability.FullProbability,
			out var selectedFull).Should().BeTrue();
		selectedFull.Should().BeSameAs(full);
	}

	[Fact]
	public void ReadDocument_RejectsProbabilityRecordProducedBySafetyScreening()
	{
		var record = RecordJson(ProbabilityGolden.Replace(
			"full-probability@4",
			"safety-screening@30",
			StringComparison.Ordinal));
		var payload = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ record + "]}";

		TerminalLobbyCache.ReadDocument(
			Utf8(payload),
			SimulatorCapabilityRegistry.Production).IsUsable.Should().BeFalse();
	}

	[Theory]
	[InlineData(false, "safety-screening@30", "safety-screening@29")]
	[InlineData(true, "full-probability@4", "full-probability@3")]
	[InlineData(false, "safety-screening@30", "foreign-simulator@1")]
	[InlineData(false, "safety-screening@30", "core-simulator@1")]
	public void ReadDocument_RejectsSchemaOneRecordsFromNonCurrentProducersAtomically(
		bool probabilityRecord,
		string currentProducer,
		string rejectedProducer)
	{
		var currentEnvelope = probabilityRecord ? ProbabilityGolden : AlreadyGolden;
		var record = RecordJson(currentEnvelope.Replace(
			currentProducer,
			rejectedProducer,
			StringComparison.Ordinal));
		var payload = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ record + "]}";

		var read = TerminalLobbyCache.ReadDocument(
			Utf8(payload),
			SimulatorCapabilityRegistry.Production);

		read.IsUsable.Should().BeFalse();
		read.Document.Should().BeNull();
	}

	[Fact]
	public void Read_RejectsObsoleteCoreSimulatorProducer()
	{
		var obsolete = AlreadyGolden.Replace(
			"safety-screening@30",
			"core-simulator@1",
			StringComparison.Ordinal);
		var document = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ RecordJson(obsolete) + "]}";

		TerminalLobbyCache.Read(
			Utf8(obsolete),
			Scenario(AlreadyDecidedIdentity()),
			SimulatorCapability.SafetyScreening)
			.IsUsable.Should().BeFalse();
		TerminalLobbyCache.ReadDocument(
			Utf8(document),
			SimulatorCapabilityRegistry.Production)
			.IsUsable.Should().BeFalse();
	}

	[Fact]
	public void CompatibilitySelection_ReusesOnlyEquivalentActorReplacementUnderCompleteActiveIdentity()
	{
		var document = TerminalLobbyCache.CreateDocument([DegenerateRecord()]);
		var originalSetup = new ActorSetupCards(
			version: 7,
			[
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Cupid),
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Defender),
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Elder)
			]);
		var replacementSetup = new ActorSetupCards(
			version: 8,
			[
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Elder),
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Defender),
				new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Cupid)
			]);
		MainRoleType[] roles =
		[
			MainRoleType.Actor,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleWerewolf
		];
		var original = new SimulationCompatibilityIdentity(
			new SimulationScenario(5, roles, originalSetup).ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var equivalentReplacement = new SimulationCompatibilityIdentity(
			new SimulationScenario(5, roles, replacementSetup).ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var differentMembership = new SimulationCompatibilityIdentity(
			new SimulationScenario(
				5,
				roles,
				new ActorSetupCards(
					[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Fox]))
				.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var differentScenario = new SimulationCompatibilityIdentity(
			new SimulationScenario(
				5,
				[
					MainRoleType.Actor,
					MainRoleType.WildChild,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleWerewolf
				],
				replacementSetup).ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		original.Should().Be(AggregateIdentity());
		equivalentReplacement.Should().Be(original);
		originalSetup.Version.Should().Be(7);
		replacementSetup.Version.Should().Be(8);
		replacementSetup.Cards.Select(card => card.Id).Should().NotIntersectWith(
			originalSetup.Cards.Select(card => card.Id));
		TerminalLobbyCache.TryGet(
			document,
			Scenario(equivalentReplacement),
			SimulatorCapability.SafetyScreening,
			out _)
			.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			document,
			Scenario(differentMembership),
			SimulatorCapability.SafetyScreening,
			out _)
			.Should().BeFalse();
		TerminalLobbyCache.TryGet(
			document,
			Scenario(differentScenario),
			SimulatorCapability.SafetyScreening,
			out _)
			.Should().BeFalse();
	}

	[Fact]
	public void PublicValuesAndEncodedForms_ContainOnlyCompactTerminalSummaryMeaning()
	{
		var record = ProbabilityRecord();
		record.CompatibilityIdentity.Should().Be(ProbabilityIdentity());
		record.GameResultFrequencies.Should().HaveCount(3);
		record.GameResultFrequencyByTurn.Should().HaveCount(2);
		using var json = JsonDocument.Parse(TerminalLobbyCache.Write(
			TerminalLobbyCache.CreateDocument([record])));
		var names = Descendants(json.RootElement)
			.Where(element => element.ValueKind == JsonValueKind.Object)
			.SelectMany(element => element.EnumerateObject().Select(property => property.Name))
			.Distinct(StringComparer.Ordinal);
		names.Should().BeSubsetOf(
		[
			"schema", "version", "records", "identity", "kind", "attempted", "completed",
			"incomplete", "results", "cells", "result", "factions", "numerator", "denominator",
			"turn", "window", "inclusiveEndingTurnCutoff", "reason", "record"
		]);
	}

	[Theory]
	[InlineData(1_000, true)]
	[InlineData(10_000, false)]
	public void Capture_AggregateTerminalVariants_ProjectsExactValuesWithoutRetainingSourceEvidence(int count, bool degenerate)
	{
		var identity = degenerate ? AggregateIdentity() : ProbabilityIdentity();
		var evidence = Evidence(identity, count, degenerate);
		var capability = degenerate
			? SimulatorCapability.SafetyScreening
			: SimulatorCapability.FullProbability;

		var record = TerminalLobbyCache.Capture(
			Scenario(identity),
			capability,
			degenerate
				? new DegenerateTerminalEvaluation(evidence)
				: new ProbabilityTerminalEvaluation(evidence));

		var aggregate = record.Should().BeAssignableTo<AggregateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(count);
		aggregate.GameResultFrequencies.Sum(x => x.Numerator).Should().Be(count);
		aggregate.GameResultFrequencyByTurn.Sum(x => x.Numerator).Should().Be(count);
		TerminalLobbyCache.Read(
			TerminalLobbyCache.Write(record),
			Scenario(identity),
			capability).IsUsable.Should().BeTrue();
	}

	[Fact]
	public void Capture_SafetyDegenerateEvidenceWithCurrentStrategy_RoundTripsSchemaOneCompactMeaning()
	{
		var identity = AggregateIdentity();
		var scenario = SimulationScenario.FromCanonical(identity.Scenario);
		var evidence = Evidence(
			identity,
			TerminalLobbyEvaluator.ScreeningAttemptCount,
			degenerate: true,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity);

		var record = TerminalLobbyCache.Capture(
			scenario,
			SimulatorCapability.SafetyScreening,
			new DegenerateTerminalEvaluation(evidence));
		var encoded = TerminalLobbyCache.Write(record);
		var read = TerminalLobbyCache.Read(
			encoded,
			scenario,
			SimulatorCapability.SafetyScreening);

		var captured = record.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		captured.CompatibilityIdentity.Should().Be(identity);
		captured.GameResultFrequencies.Select(row => row.Numerator).Should().Equal(750, 250, 0);
		captured.GameResultFrequencyByTurn.Select(cell => cell.Numerator).Should().Equal(750, 250);
		using var json = JsonDocument.Parse(encoded);
		json.RootElement.GetProperty("schema").GetString().Should().Be(TerminalLobbyCache.SchemaIdentifier);
		TerminalLobbyCache.SchemaVersion.Should().Be(1);
		json.RootElement.GetProperty("version").GetInt32().Should().Be(TerminalLobbyCache.SchemaVersion);
		Encoding.UTF8.GetString(encoded).Should().NotContain("baseline-random");
		read.IsUsable.Should().BeTrue();
		read.Record.Should().BeEquivalentTo(record);
	}

	[Theory]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.BigBadWolf, 2_000)]
	[InlineData(MainRoleType.Seer, MainRoleType.Defender, 3_000)]
	public void Capture_ActorReachableThiefDegenerateEvidence_RoundTripsSchemaOneExactCurrentRecord(
		MainRoleType offer1,
		MainRoleType offer2,
		int expectedAttemptCount)
	{
		var identity = ActorThiefIdentity(offer1, offer2);
		var scenario = Scenario(identity);
		var evidence = Evidence(
			identity,
			expectedAttemptCount,
			degenerate: true,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity);

		var record = TerminalLobbyCache.Capture(
			scenario,
			SimulatorCapability.SafetyScreening,
			new DegenerateTerminalEvaluation(evidence));
		var encoded = TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument([record]));
		var read = TerminalLobbyCache.ReadDocument(
			encoded,
			SimulatorCapabilityRegistry.Production);

		identity.Scenario.ActorSetupCards.Should().NotBeEmpty();
		identity.Scenario.ThiefOfferBranchPolicy!.Branches.Should().HaveCount(
			expectedAttemptCount / TerminalLobbyEvaluator.ScreeningAttemptCount);
		evidence.AttemptedRunCount.Should().Be(expectedAttemptCount);
		using var json = JsonDocument.Parse(encoded);
		json.RootElement.GetProperty("version").GetInt32().Should().Be(1);
		read.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			read.Document!,
			scenario,
			SimulatorCapability.SafetyScreening,
			out var roundTripped).Should().BeTrue();
		var aggregate = roundTripped.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.CompletedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.IncompleteRunCount.Should().Be(0);
		aggregate.Should().BeEquivalentTo(record);
	}

	[Theory]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.BigBadWolf, 2_000, 1_000, false)]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.BigBadWolf, 2_000, 1_000, true)]
	[InlineData(MainRoleType.Seer, MainRoleType.Defender, 3_000, 500, false)]
	[InlineData(MainRoleType.Seer, MainRoleType.Defender, 3_000, 500, true)]
	public void Capture_ActorReachableThiefDegenerateBranchWithMixedSiblingEvidence_ProjectsProvingBranchAndRoundTripsExactCurrentRecord(
		MainRoleType offer1,
		MainRoleType offer2,
		int expectedAttemptCount,
		int expectedVillagerCount,
		bool incompleteSibling)
	{
		var identity = ActorThiefIdentity(offer1, offer2);
		var scenario = Scenario(identity);
		var evidence = MixedThiefDegenerateEvidence(identity, incompleteSibling);

		var record = TerminalLobbyCache.Capture(
			scenario,
			SimulatorCapability.SafetyScreening,
			new DegenerateTerminalEvaluation(evidence));
		var encoded = TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument([record]));
		var read = TerminalLobbyCache.ReadDocument(
			encoded,
			SimulatorCapabilityRegistry.Production);

		evidence.AttemptedRunCount.Should().Be(expectedAttemptCount);
		evidence.IncompleteRunCount.Should().Be(incompleteSibling ? 1 : 0);
		read.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			read.Document!,
			scenario,
			SimulatorCapability.SafetyScreening,
			out var roundTripped).Should().BeTrue();
		var aggregate = roundTripped.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.CompletedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.IncompleteRunCount.Should().Be(0);
		aggregate.GameResultFrequencies.Sum(row => row.Numerator)
			.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.GameResultFrequencies.Select(row => row.Numerator)
			.Should().Equal(
				expectedVillagerCount,
				TerminalLobbyEvaluator.ScreeningAttemptCount - expectedVillagerCount,
				0);
		aggregate.GameResultFrequencyByTurn.Should().OnlyContain(cell => cell.EndingTurn == 1);
		aggregate.Should().BeEquivalentTo(record);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Capture_NonActorThiefDegenerateBranchWithMixedSiblingEvidence_ProjectsProvingBranchAndRoundTripsExactCurrentRecord(
		bool incompleteSibling)
	{
		var identity = NonActorThiefIdentity();
		var scenario = SimulationScenario.FromCanonical(identity.Scenario);
		var evidence = MixedThiefDegenerateEvidence(identity, incompleteSibling);

		var record = TerminalLobbyCache.Capture(
			scenario,
			SimulatorCapability.SafetyScreening,
			new DegenerateTerminalEvaluation(evidence));
		var encoded = TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument([record]));
		var read = TerminalLobbyCache.ReadDocument(
			encoded,
			SimulatorCapabilityRegistry.Production);

		identity.Scenario.ActorSetupCards.Should().BeEmpty();
		identity.Scenario.ThiefOfferBranchPolicy.Should().NotBeNull();
		evidence.AttemptedRunCount.Should().Be(3_000);
		evidence.IncompleteRunCount.Should().Be(incompleteSibling ? 1 : 0);
		read.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			read.Document!,
			scenario,
			SimulatorCapability.SafetyScreening,
			out var roundTripped).Should().BeTrue();
		var aggregate = roundTripped.Should().BeOfType<DegenerateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.CompletedRunCount.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.IncompleteRunCount.Should().Be(0);
		aggregate.GameResultFrequencies.Sum(row => row.Numerator)
			.Should().Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		aggregate.GameResultFrequencyByTurn.Should().OnlyContain(cell => cell.EndingTurn == 1);
		aggregate.Should().BeEquivalentTo(record);
	}

	[Theory]
	[InlineData(1_000, true)]
	[InlineData(10_000, false)]
	public void Capture_FullProbabilityProducerAcceptsBaselineStrategyForTerminalVariants(
		int count,
		bool degenerate)
	{
		var identity = degenerate
			? FullProbabilityDegenerateIdentity()
			: ProbabilityIdentity();
		var scenario = SimulationScenario.FromCanonical(identity.Scenario);
		var evidence = Evidence(identity, count, degenerate);

		var record = TerminalLobbyCache.Capture(
			scenario,
			SimulatorCapability.FullProbability,
			degenerate
				? new DegenerateTerminalEvaluation(evidence)
				: new ProbabilityTerminalEvaluation(evidence));

		record.Should().BeAssignableTo<AggregateTerminalCacheRecord>()
			.Which.CompatibilityIdentity.Should().Be(identity);
		TerminalLobbyCache.Read(
			TerminalLobbyCache.Write(record),
			scenario,
			SimulatorCapability.FullProbability)
			.IsUsable.Should().BeTrue();
	}

	[Theory]
	[MemberData(nameof(AggregateProducerStrategyMismatches))]
	public void Capture_RejectsDecisionStrategyFromAnotherProducer(
		SimulatorProfileIdentity producerProfile,
		DecisionStrategyIdentity wrongStrategy)
	{
		var scenario = producerProfile.Equals(
			SimulatorCapability.SafetyScreening.Identity)
			? AggregateIdentity().Scenario
			: FullProbabilityDegenerateIdentity().Scenario;
		var identity = new SimulationCompatibilityIdentity(
			scenario,
			producerProfile);
		var evidence = Evidence(
			identity,
			TerminalLobbyEvaluator.ScreeningAttemptCount,
			degenerate: true,
			wrongStrategy);
		SimulatorCapabilityRegistry.Production.TryGet(
			producerProfile,
			out var capability).Should().BeTrue();

		Action capture = () => TerminalLobbyCache.Capture(
			Scenario(identity),
			capability,
			new DegenerateTerminalEvaluation(evidence));

		capture.Should().Throw<ArgumentException>()
			.WithParameterName("evidence");
	}

	public static IEnumerable<object[]> AggregateProducerStrategyMismatches()
	{
		yield return
		[
			SimulatorCapability.FullProbability.Identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity
		];
		yield return
		[
			SimulatorCapability.SafetyScreening.Identity,
			BaselineRandomDecisionStrategy.Identity
		];
		yield return
		[
			SimulatorCapability.SafetyScreening.Identity,
			new DecisionStrategyIdentity("baseline-random", "13-splitmix64")
		];
	}

	[Theory]
	[InlineData(AggregateEvidenceMismatch.Scenario)]
	[InlineData(AggregateEvidenceMismatch.Profile)]
	[InlineData(AggregateEvidenceMismatch.Incomplete)]
	public void Capture_RejectsExactlyOneScenarioProfileOrIncompleteEvidenceMismatch(
		AggregateEvidenceMismatch mismatch)
	{
		var identity = AggregateIdentity();
		var expectedIdentity = mismatch switch
		{
			AggregateEvidenceMismatch.Scenario => new SimulationCompatibilityIdentity(
				ProbabilityIdentity().Scenario,
				identity.Profile),
			AggregateEvidenceMismatch.Profile => new SimulationCompatibilityIdentity(
				identity.Scenario,
				SimulatorCapability.FullProbability.Identity),
			AggregateEvidenceMismatch.Incomplete => identity,
			_ => throw new ArgumentOutOfRangeException(nameof(mismatch))
		};
		var evidence = mismatch == AggregateEvidenceMismatch.Incomplete
			? IncompleteEvidence(identity)
			: Evidence(
				identity,
				TerminalLobbyEvaluator.ScreeningAttemptCount,
				degenerate: true);
		SimulatorCapabilityRegistry.Production.TryGet(
			expectedIdentity.Profile,
			out var capability).Should().BeTrue();

		Action capture = () => TerminalLobbyCache.Capture(
			Scenario(expectedIdentity),
			capability,
			new DegenerateTerminalEvaluation(evidence));

		capture.Should().Throw<ArgumentException>()
			.WithParameterName("evidence");
	}

	[Fact]
	public void Capture_AggregateRejectsIdentityMismatchIncompleteEvidenceAndWrongAttemptPolicy()
	{
		var probabilityEvidence = Evidence(ProbabilityIdentity(), 10_000, degenerate: false);
		Action identityMismatch = () => TerminalLobbyCache.Capture(
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening,
			new ProbabilityTerminalEvaluation(probabilityEvidence));
		var incomplete = IncompleteEvidence(AggregateIdentity());
		Action incompleteCapture = () => TerminalLobbyCache.Capture(
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening,
			new DegenerateTerminalEvaluation(incomplete));
		var wrongCount = Evidence(AggregateIdentity(), 999, degenerate: true);
		Action wrongPolicy = () => TerminalLobbyCache.Capture(
			Scenario(AggregateIdentity()),
			SimulatorCapability.SafetyScreening,
			new DegenerateTerminalEvaluation(wrongCount));

		identityMismatch.Should().Throw<ArgumentException>();
		incompleteCapture.Should().Throw<ArgumentException>();
		wrongPolicy.Should().Throw<ArgumentException>();
	}

	private static DegenerateTerminalCacheRecord DegenerateRecord() => new(
		AggregateIdentity(),
		DegenerateRows(),
		DegenerateCells(),
		SimulatorCapability.SafetyScreening);

	private static ProbabilityTerminalCacheRecord ProbabilityRecord() => new(
		ProbabilityIdentity(),
		[
			new(new SingleFactionGameResult(Faction.Villager), 7_000, 10_000),
			new(new SingleFactionGameResult(Faction.Werewolf), 3_000, 10_000),
			new(new NoWinnerGameResult(), 0, 10_000)
		],
		[
			new(new SingleFactionGameResult(Faction.Villager), 1, VictoryCheckWindow.Dawn, 7_000, 10_000),
			new(new SingleFactionGameResult(Faction.Werewolf), 2, VictoryCheckWindow.PreNight, 3_000, 10_000)
		],
		SimulatorCapability.FullProbability);

	private static TerminalCacheGameResultFrequency[] DegenerateRows() =>
	[
		new(new SingleFactionGameResult(Faction.Villager), 750, 1000),
		new(new SingleFactionGameResult(Faction.Werewolf), 250, 1000),
		new(new NoWinnerGameResult(), 0, 1000)
	];

	private static TerminalCacheTurnWindowFrequency[] DegenerateCells() =>
	[
		new(new SingleFactionGameResult(Faction.Villager), 1, VictoryCheckWindow.Dawn, 750, 1000),
		new(new SingleFactionGameResult(Faction.Werewolf), 1, VictoryCheckWindow.PreNight, 250, 1000)
	];

	private static SimulationResultEvidence Evidence(
		SimulationCompatibilityIdentity identity,
		int count,
		bool degenerate,
		DecisionStrategyIdentity? strategy = null)
	{
		strategy ??= identity.Profile.Equals(SimulatorCapability.SafetyScreening.Identity)
			? BaselineRandomDecisionStrategy.SafetyScreeningIdentity
			: BaselineRandomDecisionStrategy.Identity;
		var villager = new SingleFactionGameResult(Faction.Villager);
		var wolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var runs = Enumerable.Range(0, count).Select(i =>
		{
			GameResult result = degenerate
				? i < 750 ? villager : wolf
				: i < 7_000 ? villager : wolf;
			var turn = degenerate ? 1 : result == villager ? 1 : result == wolf ? 2 : 3;
			var window = result == wolf ? VictoryCheckWindow.PreNight : VictoryCheckWindow.Dawn;
			return new CompletedSimulationRun(
				new RunSeedMaterial(identity, strategy, i),
				result,
				turn,
				window);
		});
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			strategy,
			runs);
		return new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[villager, wolf, noWinner]);
	}

	private static SimulationResultEvidence IncompleteEvidence(
		SimulationCompatibilityIdentity identity)
	{
		var strategy = identity.Profile.Equals(SimulatorCapability.SafetyScreening.Identity)
			? BaselineRandomDecisionStrategy.SafetyScreeningIdentity
			: BaselineRandomDecisionStrategy.Identity;
		var records = Enumerable.Range(0, 1_000)
			.Select(index => index == 999
				? (SimulationRun)new IncompleteSimulationRun(new RunSeedMaterial(
					identity,
					strategy,
					index))
				: new CompletedSimulationRun(
					new RunSeedMaterial(
						identity,
						strategy,
						index),
					new SingleFactionGameResult(Faction.Villager),
					1,
					VictoryCheckWindow.Dawn));
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			strategy,
			records);
		return new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[
				new SingleFactionGameResult(Faction.Villager),
				new SingleFactionGameResult(Faction.Werewolf),
				new NoWinnerGameResult()
			]);
	}

	private static SimulationResultEvidence MixedThiefDegenerateEvidence(
		SimulationCompatibilityIdentity identity,
		bool incompleteSibling)
	{
		var strategy = BaselineRandomDecisionStrategy.SafetyScreeningIdentity;
		var policy = identity.Scenario.ThiefOfferBranchPolicy!;
		var attemptCount = TerminalLobbyEvaluator.GetScreeningAttemptCount(identity.Scenario);
		var incompleteRunNumber = Enumerable.Range(0, attemptCount)
			.Last(run => policy.GetBranch(run) == policy.Branches[1]);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var records = Enumerable.Range(0, attemptCount).Select(run =>
		{
			var seed = new RunSeedMaterial(identity, strategy, run);
			if (incompleteSibling && run == incompleteRunNumber)
			{
				return (SimulationRun)new IncompleteSimulationRun(seed);
			}

			var result = run % 2 == 0 ? (GameResult)villager : werewolf;
			var provingBranch = policy.GetBranch(run) == policy.Branches[0];
			return new CompletedSimulationRun(
				seed,
				result,
				provingBranch ? 1 : 2,
				result == villager
					? VictoryCheckWindow.Dawn
					: VictoryCheckWindow.PreNight);
		});
		var source = new SimulationBatchSourceEvidence(
			identity.Scenario,
			identity.Profile,
			strategy,
			records);
		return new SimulationResultEvidence(
			source,
			[Faction.Villager, Faction.Werewolf],
			[villager, werewolf, noWinner]);
	}

	private static SimulationCompatibilityIdentity AggregateIdentity() =>
		ActorIdentity(villagers: 3, werewolves: 1);

	private static SimulationCompatibilityIdentity ActorThiefIdentity(
		MainRoleType offer1,
		MainRoleType offer2)
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Actor,
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			5,
			dealPool.Concat([offer1, offer2]),
			dealPool,
			offer1,
			offer2,
			new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Witch, MainRoleType.Elder]));
		return new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
	}

	private static SimulationCompatibilityIdentity NonActorThiefIdentity()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			5,
			dealPool.Concat([MainRoleType.Seer, MainRoleType.Defender]),
			dealPool,
			MainRoleType.Seer,
			MainRoleType.Defender);
		return new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
	}

	private static SimulationCompatibilityIdentity ProbabilityIdentity() =>
		Identity(6, 5, 1, SimulatorCapability.FullProbability.Identity);

	private static SimulationCompatibilityIdentity FullProbabilityDegenerateIdentity() =>
		Identity(5, 4, 1, SimulatorCapability.FullProbability.Identity);

	private static SimulationCompatibilityIdentity AlreadyDecidedIdentity() =>
		ActorIdentity(villagers: 1, werewolves: 3);

	private static SimulationCompatibilityIdentity ActorIdentity(
		int villagers,
		int werewolves) => new(
		CanonicalSimulationScenario.Parse(
			$"players=5|roles=[Actor=1,SimpleVillager={villagers},SimpleWerewolf={werewolves}]|actor=[Cupid,Defender,Elder]|rules=[]"),
		SimulatorCapability.SafetyScreening.Identity);

	private static SimulationCompatibilityIdentity Identity(
		int players,
		int villagers,
		int werewolves,
		SimulatorProfileIdentity profile) => new(
		CanonicalSimulationScenario.Parse(
			$"players={players}|roles=[SimpleVillager={villagers},SimpleWerewolf={werewolves}]|actor=[]|rules=[]"),
		profile);

	private static SimulationScenario Scenario(
		SimulationCompatibilityIdentity identity) =>
		SimulationScenario.FromCanonical(identity.Scenario);

	private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

	private static string RecordJson(string envelope)
	{
		using var json = JsonDocument.Parse(envelope);
		return json.RootElement.GetProperty("record").GetRawText();
	}

	private static string SwapResultRows(string payload)
	{
		const string first = "{\"result\":{\"kind\":0,\"factions\":[0]},\"numerator\":750,\"denominator\":1000}";
		const string second = "{\"result\":{\"kind\":0,\"factions\":[1]},\"numerator\":250,\"denominator\":1000}";
		return payload.Replace(first + "," + second, second + "," + first, StringComparison.Ordinal);
	}

	private static string SwapCells(string payload)
	{
		const string first = "{\"result\":{\"kind\":0,\"factions\":[0]},\"turn\":1,\"window\":0,\"numerator\":750,\"denominator\":1000}";
		const string second = "{\"result\":{\"kind\":0,\"factions\":[1]},\"turn\":1,\"window\":1,\"numerator\":250,\"denominator\":1000}";
		return payload.Replace(first + "," + second, second + "," + first, StringComparison.Ordinal);
	}

	private static string ReplaceFirst(string value, string oldValue, string newValue)
	{
		var index = value.IndexOf(oldValue, StringComparison.Ordinal);
		index.Should().BeGreaterThanOrEqualTo(0);
		return string.Concat(
			value.AsSpan(0, index),
			newValue,
			value.AsSpan(index + oldValue.Length));
	}

	public enum AggregateEvidenceMismatch
	{
		Scenario,
		Profile,
		Incomplete
	}

	private static IEnumerable<JsonElement> Descendants(JsonElement element)
	{
		yield return element;
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in element.EnumerateObject())
			foreach (var descendant in Descendants(property.Value))
				yield return descendant;
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in element.EnumerateArray())
			foreach (var descendant in Descendants(item))
				yield return descendant;
		}
	}
}
