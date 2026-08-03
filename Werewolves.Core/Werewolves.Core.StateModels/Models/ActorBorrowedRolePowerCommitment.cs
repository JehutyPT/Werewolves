using System.Security.Cryptography;
using System.Text;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Produces an opaque, session-keyed integrity commitment for one complete
/// private Actor borrowed Role Power commit. The public marker can therefore
/// prove the private projection without exposing low-entropy target or result
/// facts to public observers.
/// </summary>
internal static class ActorBorrowedRolePowerCommitment
{
	internal const int KeyByteCount = 32;
	internal const int IntegrityByteCount = 32;
	private const string Schema = "actor-borrowed-role-power:v1";

	internal static byte[] CreateKey() =>
		RandomNumberGenerator.GetBytes(KeyByteCount);

	internal static string EncodeKey(byte[] key)
	{
		EnsureValidKey(key);
		return Convert.ToBase64String(key);
	}

	internal static bool TryDecodeKey(string? encodedKey, out byte[] key)
	{
		key = [];
		if (string.IsNullOrWhiteSpace(encodedKey))
		{
			return false;
		}

		try
		{
			var candidate = Convert.FromBase64String(encodedKey);
			if (candidate.Length != KeyByteCount)
			{
				return false;
			}

			key = candidate;
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	internal static bool IsWellFormed(string? integrityCommitment)
	{
		if (string.IsNullOrWhiteSpace(integrityCommitment))
		{
			return false;
		}

		try
		{
			return Convert.FromBase64String(integrityCommitment).Length ==
				IntegrityByteCount;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	internal static string Create(
		byte[] key,
		IActorBorrowedRolePowerCommit commit)
	{
		EnsureValidKey(key);
		ArgumentNullException.ThrowIfNull(commit);

		using var payload = new MemoryStream();
		using (var writer = new BinaryWriter(
			payload,
			Encoding.UTF8,
			leaveOpen: true))
		{
			writer.Write(Schema);
			WriteCoordinate(writer, commit.Coordinate);
			WritePrivatePayload(writer, commit);
		}

		return Convert.ToBase64String(
			HMACSHA256.HashData(key, payload.ToArray()));
	}

	internal static bool Matches(
		byte[] key,
		IActorBorrowedRolePowerCommit commit,
		string? integrityCommitment)
	{
		if (!IsWellFormed(integrityCommitment))
		{
			return false;
		}

		var expected = Convert.FromBase64String(Create(key, commit));
		var actual = Convert.FromBase64String(integrityCommitment!);
		return CryptographicOperations.FixedTimeEquals(expected, actual);
	}

	private static void WriteCoordinate(
		BinaryWriter writer,
		ActorBorrowedRolePowerCommitCoordinate coordinate)
	{
		WritePowerIdentity(writer, coordinate.PowerIdentity);
		WriteGuid(writer, coordinate.ActorSetupCardId);
		writer.Write(coordinate.Timestamp.Ticks);
		writer.Write(coordinate.Timestamp.Offset.Ticks);
		writer.Write(coordinate.TurnNumber);
		writer.Write((int)coordinate.CurrentPhase);
		writer.Write(coordinate.PublicMarkerLogIndex);
	}

	private static void WritePrivatePayload(
		BinaryWriter writer,
		IActorBorrowedRolePowerCommit commit)
	{
		switch (commit)
		{
			case ActorBorrowedSeerCheckCommit seer:
				writer.Write((byte)1);
				WriteGuid(writer, seer.TargetPlayerId);
				writer.Write((int)seer.TargetAgentKnowledge);
				break;
			case ActorBorrowedDefenderProtectionCommit defender:
				writer.Write((byte)2);
				WriteGuid(writer, defender.TargetPlayerId);
				break;
			case ActorBorrowedFoxCheckCommit fox:
				writer.Write((byte)3);
				WriteGuid(writer, fox.CenterPlayerId);
				writer.Write((int)fox.NeighborhoodAgentKnowledge);
				WriteOptionalResourceIdentity(writer, fox.SpentResourceIdentity);
				break;
			case ActorBorrowedWitchPotionUseCommit witchUse:
				writer.Write((byte)4);
				WriteResourceIdentity(writer, witchUse.SpentResourceIdentity);
				WriteGuid(writer, witchUse.TargetPlayerId);
				break;
			case ActorBorrowedWitchPotionDeclineCommit witchDecline:
				writer.Write((byte)5);
				WriteResourceIdentity(writer, witchDecline.OfferedResourceIdentity);
				break;
			case ActorBorrowedCupidLoversCommit cupid:
				writer.Write((byte)6);
				WriteGuid(writer, cupid.FirstPlayerId);
				WriteGuid(writer, cupid.SecondPlayerId);
				// Turn-one Cupid commits are atomically recorded as Deferred and are
				// later classified by the Initial Beneficiary Closure transaction.
				// Preserve that immutable commit-boundary fact in the marker even
				// after the private projection carries the resolved disposition.
				var committedDisposition = cupid.TurnNumber == 1
					? ActorBorrowedCupidLoversDisposition
						.DeferredToInitialBeneficiaryClosure
					: cupid.Disposition;
				writer.Write((int)committedDisposition);
				break;
			case ActorBorrowedStutteringJudgeSignalSetupCommit:
				writer.Write((byte)7);
				break;
			case ActorBorrowedStutteringJudgeSignalObservationCommit observation:
				writer.Write((byte)8);
				writer.Write(observation.SignalOccurred);
				WriteOptionalResourceIdentity(
					writer,
					observation.SpentResourceIdentity);
				break;
			case ActorBorrowedVillageIdiotPardonCommit villageIdiot:
				writer.Write((byte)9);
				WriteResourceIdentity(
					writer,
					villageIdiot.SpentResourceIdentity);
				break;
			case ActorBorrowedHunterFinalShotCommit hunter:
				writer.Write((byte)10);
				writer.Write(hunter.CascadeScopeId);
				writer.Write(hunter.TriggeringPlayerIds.Count);
				foreach (var triggeringPlayerId in hunter.TriggeringPlayerIds)
				{
					WriteGuid(writer, triggeringPlayerId);
				}
				WriteGuid(writer, hunter.TargetPlayerId);
				break;
			case ActorBorrowedElderResistanceCommit elder:
				writer.Write((byte)11);
				WriteGuid(writer, elder.TargetPlayerId);
				writer.Write(elder.TriggeringNightActionLogIndex);
				writer.Write(elder.RestoringWitchSaveLogIndex.HasValue);
				if (elder.RestoringWitchSaveLogIndex is { } restorationLogIndex)
				{
					writer.Write(restorationLogIndex);
				}
				break;
			case ActorBorrowedElderSuppressionCommit elderSuppression:
				writer.Write((byte)12);
				writer.Write(
					elderSuppression.TriggeringVoteOutcomeLogIndex);
				writer.Write(elderSuppression.CascadeScopeId);
				WriteGuid(
					writer,
					elderSuppression.AnnouncementInstructionId);
				break;
			case ActorBorrowedScapegoatTieReplacementCommit tieReplacement:
				writer.Write((byte)13);
				writer.Write(tieReplacement.TriggeringVoteOutcomeLogIndex);
				writer.Write(tieReplacement.VoteOrdinal);
				writer.Write(tieReplacement.CascadeScopeId);
				break;
			case ActorBorrowedScapegoatVoterRestrictionCommit restriction:
				writer.Write((byte)14);
				writer.Write(
					restriction.TieReplacementPublicMarkerLogIndex);
				writer.Write(restriction.CascadeScopeId);
				WritePlayerIdSet(writer, restriction.CandidatePlayerIds);
				WritePlayerIdSet(writer, restriction.PermittedVoterIds);
				writer.Write(restriction.AppliesOnTurnNumber);
				WriteGuid(writer, restriction.AnnouncementInstructionId);
				break;
			case ActorBorrowedBearTamerGrowlCommit:
				writer.Write((byte)15);
				break;
			case ActorBorrowedKnightRustySwordScheduleCommit knight:
				writer.Write((byte)16);
				WriteGuid(writer, knight.TargetPlayerId);
				writer.Write(knight.WerewolfAttackEliminationLogIndex);
				writer.Write(knight.CascadeScopeId);
				break;
			default:
				throw new InvalidOperationException(
					"The Actor borrowed Role Power commit type has no integrity encoding.");
		}
	}

	private static void WritePlayerIdSet(
		BinaryWriter writer,
		IEnumerable<Guid> playerIds)
	{
		var ordered = playerIds.OrderBy(playerId => playerId).ToArray();
		writer.Write(ordered.Length);
		foreach (var playerId in ordered)
		{
			WriteGuid(writer, playerId);
		}
	}

	private static void WritePowerIdentity(
		BinaryWriter writer,
		RolePowerInstanceIdentity identity)
	{
		WriteGuid(writer, identity.ActingPlayerId);
		writer.Write((int)identity.SourceRole);
		writer.Write(identity.SourcePowerIdentifier);
		WriteGuid(writer, identity.PowerInstanceId);
		writer.Write((int)identity.PowerInstanceOrigin);
	}

	private static void WriteOptionalResourceIdentity(
		BinaryWriter writer,
		OneUseRolePowerResourceIdentity? identity)
	{
		writer.Write(identity.HasValue);
		if (identity is { } value)
		{
			WriteResourceIdentity(writer, value);
		}
	}

	private static void WriteResourceIdentity(
		BinaryWriter writer,
		OneUseRolePowerResourceIdentity identity)
	{
		WriteGuid(writer, identity.ActingPlayerId);
		writer.Write((int)identity.SourceRole);
		writer.Write(identity.SourcePowerIdentifier);
		WriteGuid(writer, identity.PowerInstanceId);
		writer.Write((int)identity.PowerInstanceOrigin);
		WriteGuid(writer, identity.OneUseResourceId);
	}

	private static void WriteGuid(BinaryWriter writer, Guid value) =>
		writer.Write(value.ToByteArray());

	private static void EnsureValidKey(byte[] key)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (key.Length != KeyByteCount)
		{
			throw new ArgumentException(
				"The Actor borrowed Role Power integrity key is invalid.",
				nameof(key));
		}
	}
}
