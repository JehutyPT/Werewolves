using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.RolePowers;

internal enum RolePowerCategory
{
	Chosen,
	Automatic,
	Reactive,
	Passive,
	Recognition,
	Communication,
}

internal readonly record struct RolePowerIdentifier
{
	internal RolePowerIdentifier(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		Value = value;
	}

	internal string Value { get; }

	public override string ToString() => Value;
}

internal sealed record RolePowerDefinition(
	RolePowerIdentifier Identifier,
	RolePowerCategory Category);

internal sealed record RolePowerInstance(
	Guid Id,
	MainRoleType SourceRole,
	RolePowerDefinition SourcePower,
	RolePowerInstanceOrigin Origin)
{
	internal static RolePowerInstance CreateNative(
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower)
	{
		ArgumentNullException.ThrowIfNull(actingPlayer);
		ArgumentNullException.ThrowIfNull(sourcePower);

		return new RolePowerInstance(
			actingPlayer.Id,
			sourceRole,
			sourcePower,
			RolePowerInstanceOrigin.Native);
	}

	internal static RolePowerInstance CreateCurrent(
		IGameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(actingPlayer);
		ArgumentNullException.ThrowIfNull(sourcePower);
		var latestSwap = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.LastOrDefault(entry => entry.PlayerId == actingPlayer.Id);
		if (latestSwap is null)
		{
			return CreateNative(actingPlayer, sourceRole, sourcePower);
		}
		if (latestSwap.NewCurrentRole != sourceRole ||
			actingPlayer.State.CurrentRole != sourceRole)
		{
			throw new InvalidOperationException(
				"The current Role Power does not match the Player's latest Permanent Role Swap.");
		}

		return new RolePowerInstance(
			latestSwap.NewPowerInstanceId,
			sourceRole,
			sourcePower,
			RolePowerInstanceOrigin.Swapped);
	}

	internal static RolePowerInstanceIdentity CreateCurrentIdentity(
		IGameSession session,
		IPlayer actingPlayer,
		MainRoleType sourceRole,
		RolePowerDefinition sourcePower)
	{
		var instance = CreateCurrent(
			session,
			actingPlayer,
			sourceRole,
			sourcePower);
		return new RolePowerInstanceIdentity(
			actingPlayer.Id,
			sourceRole,
			sourcePower.Identifier.Value,
			instance.Id,
			instance.Origin);
	}
}

internal sealed record OneUseRolePowerResource(
	Guid Id,
	RolePowerInstance OwningPowerInstance);

internal sealed record RolePowerAttempt(
	IPlayer ActingPlayer,
	MainRoleType SourceRole,
	RolePowerDefinition SourcePower,
	RolePowerInstance PowerInstance,
	OneUseRolePowerResource? OneUseResource = null);

internal sealed record RolePowerAvailabilityResult(bool IsAvailable)
{
	internal static RolePowerAvailabilityResult Allowed { get; } = new(true);
	internal static RolePowerAvailabilityResult Denied { get; } = new(false);
}

internal sealed record RolePowerExecutionContext(
	RolePowerAttempt Attempt,
	RolePowerAvailabilityResult AvailabilityResult)
{
	internal IPlayer ActingPlayer => Attempt.ActingPlayer;
	internal MainRoleType SourceRole => Attempt.SourceRole;
	internal RolePowerDefinition SourcePower => Attempt.SourcePower;
	internal RolePowerInstance PowerInstance => Attempt.PowerInstance;
	internal OneUseRolePowerResource? OneUseResource => Attempt.OneUseResource;
}

internal interface IRolePowerAvailabilityPolicy
{
	RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt);
}

internal sealed class AllowAllRolePowerAvailabilityPolicy : IRolePowerAvailabilityPolicy
{
	internal static AllowAllRolePowerAvailabilityPolicy Instance { get; } = new();

	private AllowAllRolePowerAvailabilityPolicy() { }

	public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
		RolePowerAvailabilityResult.Allowed;
}

internal sealed class RolePowerAvailabilityGateway
{
	private readonly IRolePowerAvailabilityPolicy _policy;

	internal RolePowerAvailabilityGateway(IRolePowerAvailabilityPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(policy);
		_policy = policy;
	}

	internal RolePowerExecutionContext Evaluate(RolePowerAttempt attempt)
	{
		ArgumentNullException.ThrowIfNull(attempt);
		ArgumentNullException.ThrowIfNull(attempt.ActingPlayer);
		ArgumentNullException.ThrowIfNull(attempt.SourcePower);
		ArgumentNullException.ThrowIfNull(attempt.PowerInstance);
		ArgumentNullException.ThrowIfNull(attempt.PowerInstance.SourcePower);

		if (attempt.OneUseResource is not null)
		{
			ArgumentNullException.ThrowIfNull(
				attempt.OneUseResource.OwningPowerInstance);
		}

		if (attempt.SourceRole != attempt.PowerInstance.SourceRole)
		{
			throw new ArgumentException(
				"The concrete Role Power instance must belong to the attempt's source Role.",
				nameof(attempt));
		}

		if (attempt.SourcePower != attempt.PowerInstance.SourcePower)
		{
			throw new ArgumentException(
				"The concrete Role Power instance must implement the attempt's source power.",
				nameof(attempt));
		}

		if (attempt.OneUseResource is not null &&
		    attempt.OneUseResource.OwningPowerInstance != attempt.PowerInstance)
		{
			throw new ArgumentException(
				"The One-Use Resource must belong to the attempted concrete Role Power instance.",
				nameof(attempt));
		}

		var result = _policy.Evaluate(attempt) ??
			throw new InvalidOperationException(
				"The Role Power policy must return an availability result.");

		return new RolePowerExecutionContext(attempt, result);
	}
}
