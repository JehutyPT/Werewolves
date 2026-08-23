using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Werewolves.Client.Resources;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Resources;

public class ClientStringsTests
{
	private static readonly string[] FactionUiResourceKeys =
	[
		nameof(ClientStrings.LobbyEvaluation_FactionVillager),
		nameof(ClientStrings.LobbyEvaluation_FactionWerewolf),
		nameof(ClientStrings.LobbyEvaluation_FactionWhiteWerewolf),
		nameof(ClientStrings.LobbyEvaluation_FactionPiper),
		nameof(ClientStrings.LobbyEvaluation_FactionCrossFactionLovers),
		nameof(ClientStrings.LobbyEvaluation_FactionAngel),
		nameof(ClientStrings.LobbyEvaluation_FactionPrejudicedManipulator)
	];

	private static readonly string[] ActorSetupUiResourceKeys =
	[
		nameof(ClientStrings.ActorSetup_StepLabel),
		nameof(ClientStrings.ActorSetup_Title),
		nameof(ClientStrings.ActorSetup_Description),
		nameof(ClientStrings.ActorSetup_ListAria),
		nameof(ClientStrings.ActorSetup_CardChoiceAriaFormat),
		nameof(ClientStrings.ActorSetup_SelectionCountFormat),
		nameof(ClientStrings.ActorSetup_CommitButton),
		nameof(ClientStrings.ActorSetup_IncompleteValidation),
		nameof(ClientStrings.ActorSetup_SaveFailedValidation),
		nameof(ClientStrings.ActorSetup_SummaryTitle),
		nameof(ClientStrings.RoleSelection_ReviewActorSetupButton)
	];

	private static readonly string[] PublicGroupPartitionUiResourceKeys =
	[
		nameof(ClientStrings.PublicGroupPartition_StepLabel),
		nameof(ClientStrings.PublicGroupPartition_Title),
		nameof(ClientStrings.PublicGroupPartition_Description),
		nameof(ClientStrings.PublicGroupPartition_ListAria),
		nameof(ClientStrings.PublicGroupPartition_PlayerChoiceAriaFormat),
		nameof(ClientStrings.PublicGroupPartition_FirstGroupLabel),
		nameof(ClientStrings.PublicGroupPartition_SecondGroupLabel),
		nameof(ClientStrings.PublicGroupPartition_CommitButton),
		nameof(ClientStrings.PublicGroupPartition_IncompleteValidation),
		nameof(ClientStrings.PublicGroupPartition_SaveFailedValidation),
		nameof(ClientStrings.PublicGroupPartition_SummaryTitle),
		nameof(ClientStrings.RoleSelection_ReviewPublicGroupPartitionButton)
	];

	private static readonly string[] LandingUiResourceKeys =
	[
		nameof(ClientStrings.Landing_StepLabel),
		nameof(ClientStrings.Landing_Title),
		nameof(ClientStrings.Landing_Description),
		nameof(ClientStrings.Landing_ContinueDescription),
		nameof(ClientStrings.Landing_NewGameDescription),
		nameof(ClientStrings.Landing_ContinueButton),
		nameof(ClientStrings.Landing_NewGameButton),
		nameof(ClientStrings.Landing_NewGameDialogTitle),
		nameof(ClientStrings.Landing_NewGameDialogDescription),
		nameof(ClientStrings.Landing_NewGameCancelButton),
		nameof(ClientStrings.Landing_NewGameConfirmButton),
		nameof(ClientStrings.Landing_RecentSetupsTitle),
		nameof(ClientStrings.Landing_RecentPlayersLabel),
		nameof(ClientStrings.Landing_RecentRoleCountFormat),
		nameof(ClientStrings.Landing_RecentSetupSummaryFormat),
		nameof(ClientStrings.Landing_RecentSetupSelectAriaFormat),
		nameof(ClientStrings.Landing_RecentSetupDeleteAriaFormat),
		nameof(ClientStrings.Landing_RecentRoleGroupAriaFormat),
		nameof(ClientStrings.Landing_RecentRoleBarAriaFormat),
		nameof(ClientStrings.RecentSetup_RelativeNow),
		nameof(ClientStrings.RecentSetup_RelativeMinute),
		nameof(ClientStrings.RecentSetup_RelativeMinutesFormat),
		nameof(ClientStrings.RecentSetup_RelativeHour),
		nameof(ClientStrings.RecentSetup_RelativeHoursFormat),
		nameof(ClientStrings.RecentSetup_RelativeDay),
		nameof(ClientStrings.RecentSetup_RelativeDaysFormat),
		nameof(ClientStrings.RecentSetup_RelativeWeek),
		nameof(ClientStrings.RecentSetup_RelativeWeeksFormat),
		nameof(ClientStrings.RecentSetup_RelativeMonth),
		nameof(ClientStrings.RecentSetup_RelativeMonthsFormat),
		nameof(ClientStrings.RecentSetup_RelativeYear),
		nameof(ClientStrings.RecentSetup_RelativeYearsFormat),
		nameof(ClientStrings.LobbyRoster_BackButton)
	];

	private static readonly string[] LocalizedUiResourceKeys =
	[
		.. LandingUiResourceKeys,
		nameof(ClientStrings.LobbyRoster_Title),
		nameof(ClientStrings.LobbyRoster_ResetButton),
		nameof(ClientStrings.Validation_EmptyPlayerName),
		nameof(ClientStrings.RoleSelection_Title),
		nameof(ClientStrings.RoleSelection_StartGameButton),
		nameof(ClientStrings.RoleSelection_ResetButton),
		nameof(ClientStrings.LobbyEvaluation_Title),
		nameof(ClientStrings.LobbyEvaluation_Pending),
		nameof(ClientStrings.LobbyEvaluation_AlreadyDecided),
		nameof(ClientStrings.LobbyEvaluation_Degenerate),
		nameof(ClientStrings.LobbyEvaluation_Probability),
		nameof(ClientStrings.LobbyEvaluation_CouldNotEvaluate),
		nameof(ClientStrings.LobbyEvaluation_CouldNotVerifyStartAvailable),
		nameof(ClientStrings.LobbyEvaluation_SimulatorUnavailable),
		nameof(ClientStrings.LobbyEvaluation_GameResultShared),
		nameof(ClientStrings.LobbyEvaluation_GameResultNoWinner),
		nameof(ClientStrings.LobbyEvaluation_GameResultSharedFormat),
		.. FactionUiResourceKeys,
		nameof(ClientStrings.LobbyEvaluation_FactionSeparator),
		nameof(ClientStrings.LobbyEvaluation_ReasonNoWerewolfBeneficiaries),
		nameof(ClientStrings.LobbyEvaluation_ReasonWerewolfControl),
		nameof(ClientStrings.LobbyEvaluation_ReasonMultipleVictories),
		nameof(ClientStrings.LobbyEvaluation_ReasonWhiteWerewolfSoleSurvivor),
		nameof(ClientStrings.LobbyEvaluation_NotObserved),
		nameof(ClientStrings.LobbyEvaluation_LessThanOnePercent),
		nameof(ClientStrings.LobbyEvaluation_WholePercentFormat),
		nameof(ClientStrings.LobbyEvaluation_DetailToggle),
		nameof(ClientStrings.LobbyEvaluation_DetailTitle),
		nameof(ClientStrings.LobbyEvaluation_TurnFormat),
		nameof(ClientStrings.LobbyEvaluation_FiniteBaselineCaveat),
		nameof(ClientStrings.LobbyEvaluation_Retry),
		nameof(ClientStrings.LobbyEvaluation_PendingBlock),
		nameof(ClientStrings.LobbyEvaluation_AlreadyDecidedBlock),
		nameof(ClientStrings.LobbyEvaluation_DegenerateBlock),
		nameof(ClientStrings.LobbyEvaluation_Blocked),
		nameof(ClientStrings.Dashboard_NoSession),
		nameof(ClientStrings.Dashboard_HealthDead),
		"Dashboard_VotingPowerLostPermanently",
		"Dashboard_VotingRightTemporarilyRestricted",
		nameof(ClientStrings.Dashboard_RoleKnowledgeUnknown),
		nameof(ClientStrings.Dashboard_RoleKnowledgePrivate),
		nameof(ClientStrings.Dashboard_RoleKnowledgePublic),
		nameof(ClientStrings.Dashboard_AudioMute),
		nameof(ClientStrings.Dashboard_AudioUnmute),
		nameof(ClientStrings.Benchmark_RunButton),
		nameof(ClientStrings.SelectPlayers_SubmitButton),
		nameof(ClientStrings.SelectPlayers_ListAria),
		nameof(ClientStrings.SelectOptions_Title),
		nameof(ClientStrings.SelectOptions_SelectionCountFormat),
		nameof(ClientStrings.AssignRoles_Title),
		nameof(ClientStrings.AssignRoles_SelectRolePrompt),
		nameof(ClientStrings.AssignRoles_PreviousPlayerAria),
		nameof(ClientStrings.AssignRoles_NextPlayerAria),
		nameof(ClientStrings.Common_HoldToConfirm),
		nameof(ClientStrings.Common_TapToExpand),
		nameof(ClientStrings.Dashboard_DebateTimerLabel),
		nameof(ClientStrings.Dashboard_EliminationReasonWerewolfAttack),
		nameof(ClientStrings.Dashboard_EliminationReasonDayVote),
		nameof(ClientStrings.Victory_Title),
		nameof(ClientStrings.Victory_StepLabel),
		nameof(ClientStrings.Victory_ReturnToLobbyButton),
		nameof(ClientStrings.Victory_WindowDawn),
		nameof(ClientStrings.Victory_WindowPreNight),
		.. ActorSetupUiResourceKeys,
		.. PublicGroupPartitionUiResourceKeys
	];

	[Theory]
	[InlineData("en-US", "ClientStrings.resx")]
	[InlineData("pt-PT", "ClientStrings.pt-PT.resx")]
	public void ClientStrings_ExposesNeutralAndPortugueseUiCopyThroughGeneratedAccessor(
		string cultureName,
		string resourceFileName)
	{
		var expectedValues = LoadResourceValues(resourceFileName, LocalizedUiResourceKeys);
		var previousCulture = ClientStrings.Culture;
		try
		{
			ClientStrings.Culture = CultureInfo.GetCultureInfo(cultureName);

			foreach (var key in LocalizedUiResourceKeys)
			{
				GetClientStringAccessorValue(key).Should().Be(expectedValues[key]);
			}
		}
		finally
		{
			ClientStrings.Culture = previousCulture;
		}
	}

	[Theory]
	[InlineData("en-US", "ClientStrings.resx")]
	[InlineData("pt-PT", "ClientStrings.pt-PT.resx")]
	public void ActorSetupStrings_ExposeEveryCurrentCultureThroughGeneratedAccessors(
		string cultureName,
		string resourceFileName) =>
		AssertResourceAccessors(cultureName, resourceFileName, ActorSetupUiResourceKeys);

	[Theory]
	[InlineData("en-US", "ClientStrings.resx")]
	[InlineData("pt-PT", "ClientStrings.pt-PT.resx")]
	public void PublicGroupPartitionStrings_ExposeEveryCurrentCultureThroughGeneratedAccessors(
		string cultureName,
		string resourceFileName) =>
		AssertResourceAccessors(cultureName, resourceFileName, PublicGroupPartitionUiResourceKeys);

	private static void AssertResourceAccessors(
		string cultureName,
		string resourceFileName,
		IReadOnlyCollection<string> resourceKeys)
	{
		var expectedValues = LoadResourceValues(
			resourceFileName,
			resourceKeys);
		var previousCulture = ClientStrings.Culture;
		try
		{
			ClientStrings.Culture = CultureInfo.GetCultureInfo(cultureName);

			foreach (var key in resourceKeys)
			{
				GetClientStringAccessorValue(key).Should().Be(expectedValues[key]);
			}
		}
		finally
		{
			ClientStrings.Culture = previousCulture;
		}
	}

	private static string GetClientStringAccessorValue(string key)
	{
		var property = typeof(ClientStrings).GetProperty(
			key,
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

		if (property is null)
		{
			throw new MissingMemberException(typeof(ClientStrings).FullName, key);
		}

		return property.GetValue(null) as string
			?? throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.ClientStringAccessorReturnedNonString(key));
	}

	private static IReadOnlyDictionary<string, string> LoadResourceValues(
		string resourceFileName,
		IEnumerable<string> keys)
	{
		var keySet = keys.ToHashSet(StringComparer.Ordinal);
		var resourcePath = Path.Combine(
			RepositoryRoot,
			"Werewolves.Client.Shared",
			"Resources",
			resourceFileName);

		var document = XDocument.Load(resourcePath);
		return document.Root!
			.Elements("data")
			.Where(data => keySet.Contains((string?)data.Attribute("name") ?? string.Empty))
			.ToDictionary(
				data => (string)data.Attribute("name")!,
				data => data.Element("value")?.Value ?? string.Empty,
				StringComparer.Ordinal);
	}

	private static string RepositoryRoot => ClientTestReferences.Paths.RepositoryRoot;
}
