using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.StateModels.Extensions;

public static class GameConfigValidationExtensions
{
    public static string GetDisplayMessage(this GameConfigValidationError error) =>
        GameStrings.ResourceManager.GetString($"{error.Type}ValidationError") ?? error.Message;
}
