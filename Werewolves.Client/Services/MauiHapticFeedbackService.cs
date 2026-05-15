namespace Werewolves.Client.Services;

public sealed class MauiHapticFeedbackService : IHapticFeedbackService
{
    public void Click()
    {
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
    }
}
