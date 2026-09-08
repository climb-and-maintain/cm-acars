using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Windows;
using ClimbAndMaintain.Acars.App.Configuration;
using Microsoft.Win32;

namespace ClimbAndMaintain.Acars.App.Services;

public sealed class ThemeService : IDisposable
{
    private readonly System.Windows.Application application;
    private AppearanceMode selectedMode;
    private bool disposed;

    public ThemeService(System.Windows.Application application)
    {
        this.application = application ?? throw new ArgumentNullException(nameof(application));
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppearanceMode SelectedMode => selectedMode;

    public void Apply(AppearanceMode mode)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        selectedMode = mode;
        string fileName = ResolveThemeFile(mode);

        ResourceDictionary replacement = new()
        {
            Source = new Uri($"Themes/{fileName}", UriKind.Relative),
        };
        Collection<ResourceDictionary> dictionaries = application.Resources.MergedDictionaries;
        ResourceDictionary? current = dictionaries.FirstOrDefault(static dictionary =>
            dictionary.Source?.OriginalString.Contains("Themes/Theme.", StringComparison.OrdinalIgnoreCase) == true);
        int index = current is null ? 0 : dictionaries.IndexOf(current);
        if (current is not null)
        {
            dictionaries.Remove(current);
        }

        dictionaries.Insert(index, replacement);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.Equals(eventArgs.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            ReapplyOnDispatcher();
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs)
    {
        if (selectedMode == AppearanceMode.System
            && eventArgs.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General)
        {
            ReapplyOnDispatcher();
        }
    }

    private static string ResolveThemeFile(AppearanceMode mode)
    {
        if (SystemParameters.HighContrast)
        {
            return "Theme.HighContrast.xaml";
        }

        bool useDark = mode == AppearanceMode.Dark
            || (mode == AppearanceMode.System && IsSystemDarkMode());
        return useDark ? "Theme.Dark.xaml" : "Theme.Light.xaml";
    }

    private void ReapplyOnDispatcher()
    {
        if (disposed)
        {
            return;
        }

        _ = application.Dispatcher.BeginInvoke(() => Apply(selectedMode));
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using RegistryKey? personalization = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return personalization?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
