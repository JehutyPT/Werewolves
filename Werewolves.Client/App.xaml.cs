using Werewolves.Client.Resources;
using Werewolves.Client.Services;

namespace Werewolves.Client
{
    public partial class App : Application
    {
        private readonly GameClientManager _game;
        private readonly GameplayWakeLockController _wakeLockController;

        public App()
            : this(
                new GameClientManager(),
                new GameplayWakeLockController(new DisabledScreenWakeLock()))
        {
        }

        public App(GameClientManager game, GameplayWakeLockController wakeLockController)
        {
            _game = game;
            _wakeLockController = wakeLockController;
            InitializeComponent();
            UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage()) { Title = ClientStrings.App_Title };
            window.Stopped += HandleWindowStopped;
            window.Resumed += HandleWindowResumed;
            window.Destroying += HandleWindowDestroying;
            return window;
        }

        protected override void OnResume()
        {
            base.OnResume();
            _wakeLockController.AppEnteredForeground();
            ReconcileAudioAfterResume();
        }

        private void HandleWindowStopped(object? sender, EventArgs args)
        {
            _wakeLockController.AppEnteredBackground();
        }

        private void HandleWindowResumed(object? sender, EventArgs args)
        {
            _wakeLockController.AppEnteredForeground();
            ReconcileAudioAfterResume();
        }

        private void HandleWindowDestroying(object? sender, EventArgs args)
        {
            _wakeLockController.MoveTo(GameplayWakeLockArea.None);
        }

        private void ReconcileAudioAfterResume()
        {
            _ = _game.ReconcileAudioAfterResumeAsync();
        }

        private sealed class DisabledScreenWakeLock : IScreenWakeLock
        {
            public bool KeepScreenOn { get; set; }
        }
    }
}
