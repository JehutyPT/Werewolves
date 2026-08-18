using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.StateModels.Core;

/// <summary>
/// One correlated Pending Instruction publication request, with an explicit
/// choice to retain or advance the stable recovery boundary.
/// </summary>
internal sealed class ExecutionCommit
{
	private ExecutionCommit(
		ExecutionView expected,
		ModeratorInstruction consumedInstruction,
		ModeratorResponse response,
		ModeratorInstruction nextInstruction,
		RecoveryBoundaryAdvance? recoveryBoundaryAdvance)
	{
		Expected = expected ?? throw new ArgumentNullException(nameof(expected));
		ConsumedInstruction = consumedInstruction ??
			throw new ArgumentNullException(nameof(consumedInstruction));
		Response = response ?? throw new ArgumentNullException(nameof(response));
		NextInstruction = nextInstruction ??
			throw new ArgumentNullException(nameof(nextInstruction));
		RecoveryBoundaryAdvance = recoveryBoundaryAdvance;
	}

	internal ExecutionView Expected { get; }
	internal ModeratorInstruction ConsumedInstruction { get; }
	internal ModeratorResponse Response { get; }
	internal ModeratorInstruction NextInstruction { get; }
	internal RecoveryBoundaryAdvance? RecoveryBoundaryAdvance { get; }
	internal bool AdvancesRecoveryBoundary => RecoveryBoundaryAdvance != null;

	internal static ExecutionCommit RetainRecoveryBoundary(
		ExecutionView expected,
		ModeratorInstruction consumedInstruction,
		ModeratorResponse response,
		ModeratorInstruction nextInstruction) =>
		new(
			expected,
			consumedInstruction,
			response,
			nextInstruction,
			recoveryBoundaryAdvance: null);

	internal static ExecutionCommit AdvanceRecoveryBoundary(
		ExecutionView expected,
		ModeratorInstruction consumedInstruction,
		ModeratorResponse response,
		ModeratorInstruction nextInstruction,
		AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor = null,
		DomainRecoveryCursor? domainRecoveryCursor = null) =>
		new(
			expected,
			consumedInstruction,
			response,
			nextInstruction,
			new RecoveryBoundaryAdvance(
				acceptedObservationRecoveryCursor,
				domainRecoveryCursor));
}

/// <summary>
/// The optional semantic cursor published with one newly advanced stable
/// recovery boundary. A boundary may carry at most one cursor family.
/// </summary>
internal sealed class RecoveryBoundaryAdvance
{
	internal RecoveryBoundaryAdvance(
		AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor,
		DomainRecoveryCursor? domainRecoveryCursor)
	{
		if (acceptedObservationRecoveryCursor != null &&
			domainRecoveryCursor != null)
		{
			throw new InvalidOperationException(
				"A recovery boundary cannot publish multiple recovery cursors.");
		}

		AcceptedObservationRecoveryCursor =
			acceptedObservationRecoveryCursor;
		DomainRecoveryCursor = domainRecoveryCursor;
	}

	internal AcceptedObservationRecoveryCursor?
		AcceptedObservationRecoveryCursor { get; }
	internal DomainRecoveryCursor? DomainRecoveryCursor { get; }
}
