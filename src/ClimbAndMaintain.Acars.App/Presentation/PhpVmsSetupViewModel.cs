using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using ClimbAndMaintain.Acars.App.Configuration;
using ClimbAndMaintain.Acars.App.Services;
using ClimbAndMaintain.Acars.Application.Contracts;
using System.Waf.Applications;
using System.Waf.Foundation;

namespace ClimbAndMaintain.Acars.App.Presentation;

public sealed class PhpVmsSetupViewModel : Model
{
    public const string ApiKeySecretName = "phpvms-api-key";

    private readonly DesktopSettingsCoordinator settings;
    private readonly ISecretStore secretStore;
    private readonly PhpVmsConnectionTestService connectionTestService;
    private readonly AsyncDelegateCommand testCommand;
    private readonly AsyncDelegateCommand saveCommand;
    private string baseUrl;
    private string apiKey = string.Empty;
    private string status = "Not tested.";
    private string pilotStatus = "Authenticated pilot not loaded.";
    private string lastSuccessfulRequest = "No successful request yet.";
    private bool allowInsecureLocalServer;
    private bool isBusy;

    public PhpVmsSetupViewModel(
        DesktopSettingsCoordinator settings,
        ISecretStore secretStore,
        PhpVmsConnectionTestService connectionTestService)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.connectionTestService = connectionTestService
            ?? throw new ArgumentNullException(nameof(connectionTestService));
        baseUrl = settings.Current.PhpVmsBaseUrl;
        allowInsecureLocalServer = settings.Current.AllowInsecureLocalServer;
        testCommand = new AsyncDelegateCommand(TestAsync, () => !IsBusy);
        saveCommand = new AsyncDelegateCommand(SaveAsync, () => !IsBusy);
    }

    public string BaseUrl
    {
        get => baseUrl;
        set => SetProperty(ref baseUrl, value);
    }

    public string ApiKey
    {
        get => apiKey;
        set => SetProperty(ref apiKey, value);
    }

    public bool AllowInsecureLocalServer
    {
        get => allowInsecureLocalServer;
        set => SetProperty(ref allowInsecureLocalServer, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public string PilotStatus
    {
        get => pilotStatus;
        private set => SetProperty(ref pilotStatus, value);
    }

    public string LastSuccessfulRequest
    {
        get => lastSuccessfulRequest;
        private set => SetProperty(ref lastSuccessfulRequest, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                testCommand.RaiseCanExecuteChanged();
                saveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand TestCommand => testCommand;

    public ICommand SaveCommand => saveCommand;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The setup boundary converts connection failures into safe user-facing status text.")]
    private async Task TestAsync()
    {
        IsBusy = true;
        try
        {
            Uri baseUri = CreateBaseUri();
            string? credential = string.IsNullOrWhiteSpace(ApiKey)
                ? await secretStore.GetSecretAsync(ApiKeySecretName, CancellationToken.None)
                : ApiKey;
            if (string.IsNullOrWhiteSpace(credential))
            {
                Status = "Enter an API key or save one before testing.";
                return;
            }

            PhpVmsConnectionTestResult result = await connectionTestService.TestAsync(
                baseUri,
                credential,
                AllowInsecureLocalServer,
                CancellationToken.None);
            if (result.Connection.Succeeded)
            {
                BackendPilot pilot = result.Pilot
                    ?? throw new InvalidOperationException("phpVMS authenticated but returned no pilot.");
                PilotStatus = $"Authenticated pilot: {pilot.DisplayName} ({pilot.Callsign}).";
                LastSuccessfulRequest = $"Last successful request: {DateTimeOffset.Now:G}.";
                Status = $"Connected. phpVMS server version: {result.Connection.ServerVersion ?? "not reported"}.";
            }
            else
            {
                Status = $"Connection failed. {result.Connection.FailureMessage}";
            }
        }
        catch (Exception exception)
        {
            Status = $"Connection failed. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The setup boundary must report settings failures without closing the application.")]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            Uri baseUri = CreateBaseUri();
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                await secretStore.SetSecretAsync(ApiKeySecretName, ApiKey, CancellationToken.None);
                ApiKey = string.Empty;
            }

            await settings.UpdateAsync(current => current with
            {
                PhpVmsBaseUrl = baseUri.AbsoluteUri.TrimEnd('/'),
                AllowInsecureLocalServer = AllowInsecureLocalServer,
            });
            Status = "Settings saved. The API key is protected with Windows user-scoped encryption.";
        }
        catch (Exception exception)
        {
            Status = $"Settings were not saved. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Uri CreateBaseUri()
    {
        if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidOperationException("Enter an absolute phpVMS site URL.");
        }

        if (baseUri.Scheme == Uri.UriSchemeHttp
            && (!AllowInsecureLocalServer || !baseUri.IsLoopback))
        {
            throw new InvalidOperationException(
                "phpVMS must use HTTPS. The development HTTP override accepts only localhost or another loopback address.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("The phpVMS site URL must use HTTPS or the explicit local HTTP override.");
        }

        return baseUri;
    }
}
