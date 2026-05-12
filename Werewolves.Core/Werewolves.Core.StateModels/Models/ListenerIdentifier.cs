using Werewolves.Core.StateModels.Enums;
using static Werewolves.Core.StateModels.Enums.GameHookListenerType;

namespace Werewolves.Core.StateModels.Models;


/// <summary>
/// A unified identifier for different types of hook listeners (Roles, Events, Status Effects).
/// Used to accommodate different types of listeners in the hook system.
/// </summary>
public record ListenerIdentifier
{
    /// <summary>
    /// The type of listener (MainRole, StatusEffect, or SpiritCard).
    /// </summary>
    public GameHookListenerType ListenerType { get; }
    
    /// <summary>
    /// Stores the MainRoleType, StatusEffectTypes, or EventCardType enum value as a string for better debugging/logging.
    /// </summary>
    public string ListenerId { get; }

    private ListenerIdentifier(GameHookListenerType listenerType, string listenerId)
    {
        ListenerType = listenerType;
        ListenerId = listenerId;
    }

    public static ListenerIdentifier Listener(MainRoleType mainRoleType) => new(MainRole, mainRoleType.ToString());

    public static ListenerIdentifier Listener(StatusEffectTypes statusEffect) => new(StatusEffect, statusEffect.ToString());

    public override int GetHashCode()
    {
        return HashCode.Combine(ListenerType, ListenerId);
    }

    public override string ToString()
    {
        return $"{ListenerType}:{ListenerId}";
    }

    //create impilicit conversion from MainRoleType to ListenerIdentifier
    public static implicit operator ListenerIdentifier(MainRoleType mainRoleType)
    {
        return Listener(mainRoleType);
    }

    //create implicit conversion from ListernerIdentifier to MainRoleType
    public static implicit operator MainRoleType(ListenerIdentifier listenerIdentifier)
    {
        if (listenerIdentifier.ListenerType != MainRole)
        {
            throw new InvalidCastException("ListenerIdentifier is not of type MainRole.");
        }
        if (Enum.TryParse<MainRoleType>(listenerIdentifier.ListenerId, out var roleType))
        {
            return roleType;
        }
        throw new InvalidCastException("ListenerIdentifier ListenerId could not be parsed to MainRoleType.");
    }

    //create the implicit operators for StatusEffectTypes
    public static implicit operator ListenerIdentifier(StatusEffectTypes statusEffect)
    {
        return Listener(statusEffect);
	}
    public static implicit operator StatusEffectTypes(ListenerIdentifier listenerIdentifier)
    {
        if (listenerIdentifier.ListenerType != StatusEffect)
        {
            throw new InvalidCastException("ListenerIdentifier is not of type StatusEffect.");
        }
        if (Enum.TryParse<StatusEffectTypes>(listenerIdentifier.ListenerId, out var effectType))
        {
            return effectType;
        }
        throw new InvalidCastException("ListenerIdentifier ListenerId could not be parsed to StatusEffectTypes.");
	}

	//create implicit conversion from EventCardType to ListenerIdentifier
	/*public static implicit operator ListenerIdentifier(EventCardType eventCardType)
    {
        return Listener(eventCardType);
    } */
	//create implicit conversion from ListenerIdentifier to EventCardType
	/*public static implicit operator EventCardType(ListenerIdentifier listenerIdentifier)
    {
        if (listenerIdentifier.ListenerType != GameHookListenerType.Event)
        {
            throw new InvalidCastException("ListenerIdentifier is not of type Event.");
        }
        if (Enum.TryParse<EventCardType>(listenerIdentifier.ListenerId, out var eventCardType))
        {
            return eventCardType;
        }
        throw new InvalidCastException("ListenerIdentifier ListenerId could not be parsed to EventCardType.");
    } */
}
