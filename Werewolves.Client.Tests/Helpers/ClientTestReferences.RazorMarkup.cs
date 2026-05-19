namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class RazorMarkup
	{
		public const string AssignRolesInstructionParameter = "AssignRolesInstruction Instruction";
		public const string AssignRolesPromptResource = "ClientStrings.AssignRoles_SelectRolePrompt";
		public const string AssignRolesTitleResource = "ClientStrings.AssignRoles_Title";
		public const string CreateResponseCall = "Instruction.CreateResponse";
		public const string DisabledAssignmentsIncompleteAttribute = "Disabled=\"@(!AllPlayersAssigned)\"";
		public const string DisabledParameterAttribute = "disabled=\"@Disabled\"";
		public const string DisabledSelectionInvalidAttribute = "Disabled=\"@(!IsSelectionValid)\"";
		public const string GetPublicNameCall = "GetPublicName";
		public const string HoldButtonTag = "<HoldButton";
		public const string HoldToConfirmResource = "ClientStrings.Common_HoldToConfirm";
		public const string InputViewsWithoutDashboardActionZonePredicate =
			"Instruction is not (SelectPlayersInstruction or SelectOptionsInstruction or AssignRolesInstruction)";
		public const string OnHoldCompleteHandleSubmitAttribute = "OnHoldComplete=\"HandleSubmit\"";
		public const string PointerCancelEventName = Html.Events.PointerCancel;
		public const string PointerDownEventName = Html.Events.PointerDown;
		public const string PointerLeaveEventName = Html.Events.PointerLeave;
		public const string PointerUpEventName = Html.Events.PointerUp;
		public const string OnPointerCancel = "@" + PointerCancelEventName;
		public const string OnPointerDown = "@" + PointerDownEventName;
		public const string OnPointerLeave = "@" + PointerLeaveEventName;
		public const string OnPointerUp = "@" + PointerUpEventName;
		public const string OnResponseParameterName = "OnResponse";
		public const string OptionVariable = "@option";
		public const string ParameterAttribute = "[Parameter";
		public const string RequiredParameterAttribute = "[Parameter, EditorRequired]";
		public const string RolesForAssignment = "Instruction.RolesForAssignment";
		public const string RosterAttribute = "Roster=\"Roster\"";
		public const string RosterParameter = "IReadOnlyList<DashboardRosterEntry> Roster";
		public const string SelectableOptions = "Instruction.SelectableOptions";
		public const string SelectionRange = "SelectionRange";
		public const string SelectionRangeMaximum = "SelectionRange.Maximum";
		public const string SelectOptionsCountResource = "ClientStrings.SelectOptions_SelectionCountFormat";
		public const string SelectOptionsInstructionParameter = "SelectOptionsInstruction Instruction";
		public const string SelectOptionsTitleResource = "ClientStrings.SelectOptions_Title";
		public const string SelectPlayersInstructionBranch = "is SelectPlayersInstruction selectPlayersInstruction";
		public const string SelectPlayersViewTag = "<SelectPlayersView";
		public const string ShouldRenderDashboardActionZone = "ShouldRenderDashboardActionZone";
		public const string SubmitButtonResource = "ClientStrings.Dashboard_ContinueButton";
		public const string SubmitButtonResourceLabelAttribute = "Label=\"@ClientStrings.Dashboard_ContinueButton\"";

		public static string DashboardActionFooterWithHoldButtonPattern =>
			$@"(?s)<footer class=""{Css.Classes.DashboardActionZone}"">\s*{HoldButtonTag}";

		public const string EventCallbackModeratorResponseParameterSuffix = "<ModeratorResponse> OnResponse";
		public const string EventCallbackOnHoldCompleteParameterSuffix = " OnHoldComplete";
		public const string PlayerAssignmentSource = "Instruction.PlayersForAssignment";
	}
}
