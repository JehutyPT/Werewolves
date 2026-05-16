namespace Werewolves.Client.Services;

public interface IHapticFeedbackService
{
    void Click();
}

public static class HapticFeedbackServiceExtensions
{
    public static void TryClick(this IHapticFeedbackService hapticFeedback)
    {
        try
        {
            hapticFeedback.Click();
        }
        catch (Exception)
        {
            // Haptics are optional; platform failures must not abort game actions.
        }
    }
}
