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
	private static readonly string[] PortugueseUiResourceKeys =
	[
		nameof(ClientStrings.LobbyRoster_Title),
		nameof(ClientStrings.Validation_EmptyPlayerName),
		nameof(ClientStrings.RoleSelection_Title),
		nameof(ClientStrings.RoleSelection_StartGameButton),
		nameof(ClientStrings.LobbyEvaluation_Title),
		nameof(ClientStrings.LobbyEvaluation_Pending),
		nameof(ClientStrings.LobbyEvaluation_AlreadyDecided),
		nameof(ClientStrings.LobbyEvaluation_Degenerate),
		nameof(ClientStrings.LobbyEvaluation_Probability),
		nameof(ClientStrings.LobbyEvaluation_CouldNotEvaluate),
		nameof(ClientStrings.LobbyEvaluation_SimulatorUnavailable),
		nameof(ClientStrings.LobbyEvaluation_GameResultShared),
		nameof(ClientStrings.LobbyEvaluation_GameResultNoWinner),
		nameof(ClientStrings.LobbyEvaluation_GameResultSharedFormat),
		nameof(ClientStrings.LobbyEvaluation_FactionVillager),
		nameof(ClientStrings.LobbyEvaluation_FactionWerewolf),
		nameof(ClientStrings.LobbyEvaluation_FactionWhiteWerewolf),
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
		nameof(ClientStrings.Victory_WindowPreNight)
	];

	[Fact]
	public void ClientStrings_ExposesPortugueseUiCopyThroughGeneratedAccessor()
	{
		var expectedValues = LoadResourceValues("ClientStrings.pt-PT.resx", PortugueseUiResourceKeys);
		var previousCulture = ClientStrings.Culture;
		try
		{
			ClientStrings.Culture = CultureInfo.GetCultureInfo("pt-PT");

			foreach (var key in PortugueseUiResourceKeys)
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
