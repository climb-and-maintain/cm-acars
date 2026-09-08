using System.Windows.Input;
using ClimbAndMaintain.Acars.App.Configuration;
using System.Waf.Applications;
using System.Waf.Foundation;

namespace ClimbAndMaintain.Acars.App.Presentation;

public sealed class FirstRunWizardViewModel : Model
{
    private const int LastStep = 6;
    private readonly DesktopSettingsCoordinator settings;
    private readonly DelegateCommand backCommand;
    private readonly DelegateCommand nextCommand;
    private readonly AsyncDelegateCommand finishCommand;
    private int step;
    private string status = "You can close setup at any time. The main application remains available.";

    public FirstRunWizardViewModel(
        PhpVmsSetupViewModel phpVms,
        SimulatorSetupViewModel simulator,
        SettingsPageViewModel appearance,
        DesktopSettingsCoordinator settings,
        BrandingOptions branding)
    {
        PhpVms = phpVms ?? throw new ArgumentNullException(nameof(phpVms));
        Simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Branding = branding ?? throw new ArgumentNullException(nameof(branding));
        backCommand = new DelegateCommand(Back, () => Step > 0);
        nextCommand = new DelegateCommand(Next, () => Step < LastStep);
        finishCommand = new AsyncDelegateCommand(FinishAsync, () => Step == LastStep);
        SetUpLaterCommand = new DelegateCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? RequestClose;

    public PhpVmsSetupViewModel PhpVms { get; }

    public SimulatorSetupViewModel Simulator { get; }

    public SettingsPageViewModel Appearance { get; }

    public BrandingOptions Branding { get; }

    public int Step
    {
        get => step;
        private set
        {
            if (!SetProperty(ref step, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ProgressText));
            RaisePropertyChanged(nameof(IsWelcomeStep));
            RaisePropertyChanged(nameof(IsServerStep));
            RaisePropertyChanged(nameof(IsCredentialStep));
            RaisePropertyChanged(nameof(IsConnectionStep));
            RaisePropertyChanged(nameof(IsSimulatorStep));
            RaisePropertyChanged(nameof(IsAppearanceStep));
            RaisePropertyChanged(nameof(IsReadyStep));
            backCommand.RaiseCanExecuteChanged();
            nextCommand.RaiseCanExecuteChanged();
            finishCommand.RaiseCanExecuteChanged();
        }
    }

    public string ProgressText => $"Step {Step + 1} of {LastStep + 1}";

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public bool IsWelcomeStep => Step == 0;

    public bool IsServerStep => Step == 1;

    public bool IsCredentialStep => Step == 2;

    public bool IsConnectionStep => Step == 3;

    public bool IsSimulatorStep => Step == 4;

    public bool IsAppearanceStep => Step == 5;

    public bool IsReadyStep => Step == LastStep;

    public ICommand BackCommand => backCommand;

    public ICommand NextCommand => nextCommand;

    public ICommand FinishCommand => finishCommand;

    public ICommand SetUpLaterCommand { get; }

    private void Back()
    {
        Step--;
    }

    private void Next()
    {
        Step++;
    }

    private async Task FinishAsync()
    {
        await settings.UpdateAsync(current => current with { SetupCompleted = true });
        Status = "Setup completed. You can change every setting later.";
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
