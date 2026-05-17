namespace Werewolves.Client.Services;

public sealed class MauiHapticFeedbackService : IHapticFeedbackService
{
    public void Click()
    {
        MainThread.BeginInvokeOnMainThread(() => Perform(HapticFeedbackType.Click));
    }

    public void LongPress()
    {
        MainThread.BeginInvokeOnMainThread(() => Perform(HapticFeedbackType.LongPress));
    }

    private static void Perform(HapticFeedbackType hapticFeedbackType)
    {
        try
        {
            HapticFeedback.Default.Perform(hapticFeedbackType);
        }
        catch (Exception)
        {
            // Haptics are optional; platform failures must not abort game actions.
        }
    }
}
