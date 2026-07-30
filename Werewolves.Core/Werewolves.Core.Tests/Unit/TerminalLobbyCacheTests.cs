using System.Text;
using System.Text.Json;
using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class TerminalLobbyCacheTests
{
	private const string AlreadyGolden =
		"{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{\"identity\":\"profile=safety-screening@18|players=5|roles=[SimpleVillager=2,SimpleWerewolf=3]|actor=[]|rules=[]\",\"kind\":\"alreadyDecided\",\"result\":{\"kind\":0,\"factions\":[1]},\"reason\":2}}";

	private const string DegenerateGolden =
		"{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{\"identity\":\"profile=safety-screening@18|players=5|roles=[SimpleVillager=4,SimpleWerewolf=1]|actor=[]|rules=[]\",\"kind\":\"degenerate\",\"attempted\":1000,\"completed\":1000,\"incomplete\":0,\"results\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"numerator\":750,\"denominator\":1000},{\"result\":{\"kind\":0,\"factions\":[1]},\"numerator\":250,\"denominator\":1000},{\"result\":{\"kind\":2,\"factions\":[]},\"numerator\":0,\"denominator\":1000}],\"cells\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"turn\":1,\"window\":0,\"numerator\":750,\"denominator\":1000},{\"result\":{\"kind\":0,\"factions\":[1]},\"turn\":1,\"window\":1,\"numerator\":250,\"denominator\":1000}],\"inclusiveEndingTurnCutoff\":1}}";

	private const string ProbabilityGolden =
		"{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"record\":{\"identity\":\"profile=full-probability@4|players=6|roles=[SimpleVillager=5,SimpleWerewolf=1]|actor=[]|rules=[]\",\"kind\":\"probability\",\"attempted\":10000,\"completed\":10000,\"incomplete\":0,\"results\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"numerator\":7000,\"denominator\":10000},{\"result\":{\"kind\":0,\"factions\":[1]},\"numerator\":3000,\"denominator\":10000},{\"result\":{\"kind\":2,\"factions\":[]},\"numerator\":0,\"denominator\":10000}],\"cells\":[{\"result\":{\"kind\":0,\"factions\":[0]},\"turn\":1,\"window\":0,\"numerator\":7000,\"denominator\":10000},{\"result\":{\"kind\":0,\"factions\":[1]},\"turn\":2,\"window\":1,\"numerator\":3000,\"denominator\":10000}]}}";

	[Fact]
	public void Capture_AlreadyDecided_RequiresExactSafetyProducerClassifierMeaning()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf
			]);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var evaluation = new TerminalLobbyEvaluator()
			.Evaluate(
				scenario,
				SimulatorCapability.SafetyScreening,
				LobbyEvaluationDepth.DegenerateScreeningOnly)
			.Should().BeOfType<AlreadyDecidedTerminalEvaluation>().Subject;

		var record = TerminalLobbyCache.Capture(identity, evaluation);

		var decided = record.Should().BeOfType<AlreadyDecidedTerminalCacheRecord>().Subject;
		decided.CompatibilityIdentity.Should().Be(identity);
		decided.GameResult.Should().Be(evaluation.GameResult);
		decided.Reason.Should().Be(evaluation.Reason);
		classification.Cacheability.Should().BeNull(
			"already-decided classification deliberately has no Cacheability result");
		Action mismatched = () => TerminalLobbyCache.Capture(identity,
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
			AlreadyDecidedReason.WerewolfControlShortcut);
		Action noWinner = () => new AlreadyDecidedTerminalCacheRecord(
			AlreadyDecidedIdentity(),
			new NoWinnerGameResult(),
			AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied);
		Action impossibleShared = () => new AlreadyDecidedTerminalCacheRecord(
			AlreadyDecidedIdentity(),
			new SharedVictoryGameResult([Faction.Villager, Faction.Werewolf]),
			AlreadyDecidedReason.MultipleLobbyExitVictoryPredicatesSatisfied);
		Action notAlreadyDecided = () => new AlreadyDecidedTerminalCacheRecord(
			AggregateIdentity(),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);
		var staleAggregateIdentity = new SimulationCompatibilityIdentity(
			AggregateIdentity().Scenario,
			new SimulatorProfileIdentity("other-simulator", "1"));
		Action staleAggregate = () => new DegenerateTerminalCacheRecord(
			staleAggregateIdentity,
			DegenerateRows(),
			DegenerateCells());

		obsoleteProducer.Should().Throw<ArgumentException>();
		noWinner.Should().Throw<ArgumentException>();
		impossibleShared.Should().Throw<ArgumentException>();
		notAlreadyDecided.Should().Throw<ArgumentException>();
		staleAggregate.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData("{\"kind\":1,\"factions\":[0,1]}", 3)]
	[InlineData("{\"kind\":2,\"factions\":[]}", 3)]
	public void Read_RejectsCurrentProfileSharedAndNoWinnerAlreadyDecidedPayloads(
		string resultJson,
		int reason)
	{
		var impossibleRecord = AlreadyGolden
			.Replace(
				"{\"kind\":0,\"factions\":[1]}",
				resultJson,
				StringComparison.Ordinal)
			.Replace("\"reason\":2", $"\"reason\":{reason}", StringComparison.Ordinal);

		TerminalLobbyCache.Read(Utf8(impossibleRecord), AlreadyDecidedIdentity()).IsUsable.Should().BeFalse();
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
		var action = () => TerminalLobbyCache.Read(Utf8(payload), AggregateIdentity());

		action.Should().NotThrow();
		action().IsUsable.Should().BeFalse();
	}

	[Theory]
	[InlineData("players=5|roles=[SimpleVillager=5]|actor=[]|rules=[]")]
	[InlineData("players=5|roles=[Cupid=1,SimpleVillager=3,SimpleWerewolf=1]|actor=[]|rules=[]")]
	[InlineData("players=5|roles=[SimpleVillager=3,SimpleWerewolf=1,WildChild=1]|actor=[Cupid,Defender,Elder]|rules=[]")]
	public void Read_RejectsRulesAppOrSimulatorUnsupportedCanonicalIdentity(
		string canonicalScenario)
	{
		var payload = DegenerateGolden.Replace(
			AggregateIdentity().Scenario.ToString(),
			canonicalScenario,
			StringComparison.Ordinal);

		TerminalLobbyCache.Read(Utf8(payload), AggregateIdentity())
			.IsUsable.Should().BeFalse();
	}

	[Fact]
	public void AlreadyDecidedRecord_HasReviewedGoldenBytesAndRoundTrips()
	{
		var record = new AlreadyDecidedTerminalCacheRecord(
			AlreadyDecidedIdentity(),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);

		TerminalLobbyCache.Write(record).Should().Equal(Utf8(AlreadyGolden));
		var read = TerminalLobbyCache.Read(Utf8(AlreadyGolden), AlreadyDecidedIdentity());
		read.IsUsable.Should().BeTrue();
		read.Record.Should().BeEquivalentTo(record);
	}

	[Fact]
	public void DegenerateRecord_HasReviewedGoldenBytesAndRoundTripsExactCompactDistribution()
	{
		var record = DegenerateRecord();

		TerminalLobbyCache.Write(record).Should().Equal(Utf8(DegenerateGolden));
		var read = TerminalLobbyCache.Read(Utf8(DegenerateGolden), AggregateIdentity());

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
	public void ProbabilityRecord_HasReviewedGoldenBytesAndRoundTripsExactCompactDistribution()
	{
		var record = ProbabilityRecord();

		TerminalLobbyCache.Write(record).Should().Equal(Utf8(ProbabilityGolden));
		var read = TerminalLobbyCache.Read(Utf8(ProbabilityGolden), ProbabilityIdentity());

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
		var parsed = TerminalLobbyCache.ReadDocument(documentBytes);
		var local = TerminalLobbyCache.Read(TerminalLobbyCache.Write(degenerate), AggregateIdentity());

		parsed.IsUsable.Should().BeTrue();
		local.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(parsed.Document!, AggregateIdentity(), out var selectedRecord).Should().BeTrue();
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
	public void AggregateConstructors_RejectIncompleteOrInconsistentCurrentProfileMeaning(string mutation)
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

		Action construct = () => new DegenerateTerminalCacheRecord(AggregateIdentity(), rows, cells);

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
		var action = () => TerminalLobbyCache.Read(Utf8(payload), AlreadyDecidedIdentity());

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
	    yield return [AlreadyGolden.Replace("safety-screening@18", "safety screening@18", StringComparison.Ordinal)];
	    yield return [AlreadyGolden.Replace("safety-screening@18", "safety-screening@17", StringComparison.Ordinal)];
		yield return [AlreadyGolden.Replace("\"schema\":\"terminal-lobby-cache\",\"version\":1", "\"version\":1,\"schema\":\"terminal-lobby-cache\"", StringComparison.Ordinal)];
	}

	[Theory]
	[MemberData(nameof(InvalidAggregatePayloads))]
	public void Read_RejectsInvalidAggregateInventoryPartitionEnumsAndOrdering(string payload)
	{
		TerminalLobbyCache.Read(Utf8(payload), AggregateIdentity()).IsUsable.Should().BeFalse();
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
		TerminalLobbyCache.Read(Utf8(payload), ProbabilityIdentity()).IsUsable.Should().BeFalse();
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

		rows.Sum(row => row.GetProperty("numerator").GetInt32()).Should().Be(denominator);
		TerminalLobbyCache.Read(
			Utf8(payload),
			degenerate ? AggregateIdentity() : ProbabilityIdentity())
			.IsUsable.Should().BeFalse();
	}

	[Theory]
	[MemberData(nameof(InvalidDocumentEnvelopes))]
	public void ReadDocument_RejectsMalformedOrAmbiguousEnvelopeAtomically(string payload)
	{
		var action = () => TerminalLobbyCache.ReadDocument(Utf8(payload));

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
	        "safety-screening@18",
			"safety-screening@17",
			StringComparison.Ordinal);
		Action duplicateConstructor = () => TerminalLobbyCache.CreateDocument(
			[DegenerateRecord(), DegenerateRecord()]);

		duplicateConstructor.Should().Throw<ArgumentException>();
		foreach (var payload in new[] { "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":[{}]}", malformed, duplicate, reversed, stale })
		{
			var action = () => TerminalLobbyCache.ReadDocument(Utf8(payload));
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

		var read = TerminalLobbyCache.ReadDocument(Utf8(payload));

		read.Rejection.Should().BeNull();
		read.Document!.Records.Select(record => record.CompatibilityIdentity.Profile.ToString())
	        .Should().Equal("full-probability@4", "safety-screening@18", "safety-screening@18");
	}

	[Fact]
	public void ReadDocument_RejectsProbabilityRecordProducedBySafetyScreening()
	{
		var record = RecordJson(ProbabilityGolden.Replace(
			"full-probability@4",
	        "safety-screening@18",
			StringComparison.Ordinal));
		var payload = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ record + "]}";

		TerminalLobbyCache.ReadDocument(Utf8(payload)).IsUsable.Should().BeFalse();
	}

	[Theory]
	[InlineData(false, "safety-screening@18", "safety-screening@17")]
	[InlineData(true, "full-probability@4", "full-probability@3")]
	[InlineData(false, "safety-screening@18", "foreign-simulator@1")]
	[InlineData(false, "safety-screening@18", "core-simulator@1")]
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

		var read = TerminalLobbyCache.ReadDocument(Utf8(payload));

		read.IsUsable.Should().BeFalse();
		read.Document.Should().BeNull();
	}

	[Fact]
	public void Read_RejectsObsoleteCoreSimulatorProducer()
	{
		var obsolete = AlreadyGolden.Replace(
	        "safety-screening@18",
			"core-simulator@1",
			StringComparison.Ordinal);
		var document = "{\"schema\":\"terminal-lobby-cache\",\"version\":1,\"records\":["
			+ RecordJson(obsolete) + "]}";

		TerminalLobbyCache.Read(Utf8(obsolete), AlreadyDecidedIdentity())
			.IsUsable.Should().BeFalse();
		TerminalLobbyCache.ReadDocument(Utf8(document))
			.IsUsable.Should().BeFalse();
	}

	[Fact]
	public void CompatibilitySelection_RequiresTheCompleteActiveIdentity()
	{
		var document = TerminalLobbyCache.CreateDocument([DegenerateRecord()]);
		var stale = new SimulationCompatibilityIdentity(
			AggregateIdentity().Scenario,
	        new SimulatorProfileIdentity("safety-screening", "17"));

		TerminalLobbyCache.TryGet(document, AggregateIdentity(), out _).Should().BeTrue();
		TerminalLobbyCache.TryGet(document, stale, out _).Should().BeFalse();
		TerminalLobbyCache.Read(TerminalLobbyCache.Write(DegenerateRecord()), stale).IsUsable.Should().BeFalse();
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

		var record = TerminalLobbyCache.Capture(identity, degenerate
			? new DegenerateTerminalEvaluation(evidence)
			: new ProbabilityTerminalEvaluation(evidence));

		var aggregate = record.Should().BeAssignableTo<AggregateTerminalCacheRecord>().Subject;
		aggregate.AttemptedRunCount.Should().Be(count);
		aggregate.GameResultFrequencies.Sum(x => x.Numerator).Should().Be(count);
		aggregate.GameResultFrequencyByTurn.Sum(x => x.Numerator).Should().Be(count);
		TerminalLobbyCache.Read(TerminalLobbyCache.Write(record), identity).IsUsable.Should().BeTrue();
	}

	[Fact]
	public void Capture_SafetyDegenerateEvidenceWithCurrentStrategy_RoundTripsSchemaOneCompactMeaning()
	{
		var identity = AggregateIdentity();
		var evidence = Evidence(
			identity,
			TerminalLobbyEvaluator.ScreeningAttemptCount,
			degenerate: true,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity);

		var record = TerminalLobbyCache.Capture(
			identity,
			new DegenerateTerminalEvaluation(evidence));
		var encoded = TerminalLobbyCache.Write(record);
		var read = TerminalLobbyCache.Read(encoded, identity);

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
	[InlineData(1_000, true)]
	[InlineData(10_000, false)]
	public void Capture_FullProbabilityProducerAcceptsBaselineStrategyForTerminalVariants(
		int count,
		bool degenerate)
	{
		var sourceIdentity = degenerate ? AggregateIdentity() : ProbabilityIdentity();
		var identity = CurrentIdentity(sourceIdentity, "full-probability");
		var evidence = Evidence(identity, count, degenerate);

		var record = TerminalLobbyCache.Capture(
			identity,
			degenerate
				? new DegenerateTerminalEvaluation(evidence)
				: new ProbabilityTerminalEvaluation(evidence));

		record.Should().BeAssignableTo<AggregateTerminalCacheRecord>()
			.Which.CompatibilityIdentity.Should().Be(identity);
		TerminalLobbyCache.Read(TerminalLobbyCache.Write(record), identity)
			.IsUsable.Should().BeTrue();
	}

	[Theory]
	[MemberData(nameof(AggregateProducerStrategyMismatches))]
	public void Capture_RejectsDecisionStrategyFromAnotherProducer(
		SimulatorProfileIdentity producerProfile,
		DecisionStrategyIdentity wrongStrategy)
	{
		var identity = new SimulationCompatibilityIdentity(
			AggregateIdentity().Scenario,
			producerProfile);
		var evidence = Evidence(
			identity,
			TerminalLobbyEvaluator.ScreeningAttemptCount,
			degenerate: true,
			wrongStrategy);

		Action capture = () => TerminalLobbyCache.Capture(
			identity,
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
				new DecisionStrategyIdentity("baseline-random", "9-splitmix64")
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
			AggregateEvidenceMismatch.Profile => CurrentIdentity(identity, "full-probability"),
			AggregateEvidenceMismatch.Incomplete => identity,
			_ => throw new ArgumentOutOfRangeException(nameof(mismatch))
		};
		var evidence = mismatch == AggregateEvidenceMismatch.Incomplete
			? IncompleteEvidence(identity)
			: Evidence(
				identity,
				TerminalLobbyEvaluator.ScreeningAttemptCount,
				degenerate: true);

		Action capture = () => TerminalLobbyCache.Capture(
			expectedIdentity,
			new DegenerateTerminalEvaluation(evidence));

		capture.Should().Throw<ArgumentException>()
			.WithParameterName("evidence");
	}

	[Fact]
	public void Capture_AggregateRejectsIdentityMismatchIncompleteEvidenceAndWrongAttemptPolicy()
	{
		var probabilityEvidence = Evidence(ProbabilityIdentity(), 10_000, degenerate: false);
		Action identityMismatch = () => TerminalLobbyCache.Capture(
			AggregateIdentity(),
			new ProbabilityTerminalEvaluation(probabilityEvidence));
		var incomplete = IncompleteEvidence(AggregateIdentity());
		Action incompleteCapture = () => TerminalLobbyCache.Capture(
			AggregateIdentity(),
			new DegenerateTerminalEvaluation(incomplete));
		var wrongCount = Evidence(AggregateIdentity(), 999, degenerate: true);
		Action wrongPolicy = () => TerminalLobbyCache.Capture(
			AggregateIdentity(),
			new DegenerateTerminalEvaluation(wrongCount));

		identityMismatch.Should().Throw<ArgumentException>();
		incompleteCapture.Should().Throw<ArgumentException>();
		wrongPolicy.Should().Throw<ArgumentException>();
	}

	private static DegenerateTerminalCacheRecord DegenerateRecord() => new(
		AggregateIdentity(),
		DegenerateRows(),
		DegenerateCells());

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
		]);

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

	private static SimulationCompatibilityIdentity AggregateIdentity() =>
		Identity(5, 4, 1, SimulatorCapability.SafetyScreening.Identity);

	private static SimulationCompatibilityIdentity ProbabilityIdentity() =>
		Identity(6, 5, 1, SimulatorCapability.FullProbability.Identity);

	private static SimulationCompatibilityIdentity AlreadyDecidedIdentity() =>
		Identity(5, 2, 3, SimulatorCapability.SafetyScreening.Identity);

	private static SimulationCompatibilityIdentity CurrentIdentity(
		SimulationCompatibilityIdentity sourceIdentity,
		string profileId) => new(
			sourceIdentity.Scenario,
			profileId switch
			{
				"safety-screening" => SimulatorCapability.SafetyScreening.Identity,
				"full-probability" => SimulatorCapability.FullProbability.Identity,
				_ => throw new ArgumentOutOfRangeException(nameof(profileId))
			});

	private static SimulationCompatibilityIdentity Identity(
		int players,
		int villagers,
		int werewolves,
		SimulatorProfileIdentity profile) => new(
		CanonicalSimulationScenario.Parse(
			$"players={players}|roles=[SimpleVillager={villagers},SimpleWerewolf={werewolves}]|actor=[]|rules=[]"),
		profile);

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
