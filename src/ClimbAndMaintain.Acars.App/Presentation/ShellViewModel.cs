using System.Collections.ObjectModel;
using ClimbAndMaintain.Acars.App.Configuration;
using System.Waf.Foundation;

namespace ClimbAndMaintain.Acars.App.Presentation;

public sealed class ShellViewModel : Model
{
    private PageViewModel selectedPage;

    public ShellViewModel(
        DashboardPageViewModel dashboard,
        FlightsPageViewModel flights,
        FlightLogPageViewModel flightLog,
        DiagnosticsPageViewModel diagnostics,
        SettingsPageViewModel settings,
        AboutPageViewModel about,
        BrandingOptions branding)
    {
        Pages = [dashboard, flights, flightLog, diagnostics, settings, about];
        selectedPage = dashboard;
        Branding = branding ?? throw new ArgumentNullException(nameof(branding));
    }

    public ObservableCollection<PageViewModel> Pages { get; }

    public BrandingOptions Branding { get; }

    public PageViewModel SelectedPage
    {
        get => selectedPage;
        set => SetProperty(ref selectedPage, value);
    }
}
