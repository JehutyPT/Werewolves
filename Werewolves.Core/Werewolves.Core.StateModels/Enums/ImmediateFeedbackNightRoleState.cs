namespace Werewolves.Core.StateModels.Enums;

internal enum ImmediateFeedbackNightRoleState
{
	AwaitingAwakeConfirmation,
	AwaitingTargetSelection,
	AwaitingModeratorFeedback,
	AwaitingSleepConfirmation,
	Asleep
}