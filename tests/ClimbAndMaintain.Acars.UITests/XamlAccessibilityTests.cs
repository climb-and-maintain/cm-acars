using System.Xml.Linq;

namespace ClimbAndMaintain.Acars.UITests;

public sealed class XamlAccessibilityTests
{
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] InteractiveControlNames =
        ["Button", "CheckBox", "ComboBox", "ListBox", "PasswordBox", "TextBox"];

    private static readonly string[] SemanticBrushKeys =
    [
        "WindowBackgroundBrush",
        "SurfaceBrush",
        "SurfaceMutedBrush",
        "TextBrush",
        "MutedTextBrush",
        "BorderBrush",
        "AccentBrush",
        "AccentTextBrush",
        "SuccessBrush",
        "WarningBrush",
        "ErrorBrush",
    ];

    [Fact]
    public void EveryInteractiveViewControlUsesNativeWpfAndHasAnAccessibleName()
    {
        foreach (XamlFile view in LoadViews())
        {
            XElement[] controls = view.Document
                .Descendants()
                .Where(element => InteractiveControlNames.Contains(element.Name.LocalName, StringComparer.Ordinal))
                .ToArray();
            Assert.NotEmpty(controls);

            foreach (XElement control in controls)
            {
                Assert.Equal(PresentationNamespace, control.Name.NamespaceName);
                string? accessibleName = (string?)control.Attribute("AutomationProperties.Name");
                Assert.False(
                    string.IsNullOrWhiteSpace(accessibleName),
                    $"{view.RelativePath}: <{control.Name.LocalName}> {ElementIdentity(control)} needs an explicit AutomationProperties.Name.");
            }
        }
    }

    [Fact]
    public void LiteralAutomationIdsAreUniqueAndCriticalControlsKeepStableTypes()
    {
        XamlFile[] views = LoadViews().ToArray();
        var literalIds = views
            .SelectMany(view => view.Document
                .Descendants()
                .Select(element => new
                {
                    View = view.RelativePath,
                    Element = element,
                    Id = (string?)element.Attribute("AutomationProperties.AutomationId"),
                }))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Id) && !item.Id!.StartsWith('{'))
            .ToArray();
        string[] duplicates = literalIds
            .GroupBy(static item => item.Id!, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        Assert.Empty(duplicates);

        XDocument shell = views.Single(static view => view.RelativePath.EndsWith("ShellWindow.xaml", StringComparison.Ordinal)).Document;
        IReadOnlyDictionary<string, string> criticalTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PrimaryNavigation"] = "ListBox",
            ["SimConnect.Discover"] = "Button",
            ["SimConnect.Candidates"] = "ComboBox",
            ["SimConnect2020.Path"] = "TextBox",
            ["SimConnect2020.Validate"] = "Button",
            ["SimConnect2020.Test"] = "Button",
            ["SimConnect2024.Path"] = "TextBox",
            ["SimConnect2024.Validate"] = "Button",
            ["SimConnect2024.Test"] = "Button",
            ["Replay.Open"] = "Button",
            ["Replay.Speed"] = "ComboBox",
            ["Replay.Start"] = "Button",
            ["Diagnostics.Copy"] = "Button",
            ["Diagnostics.Export"] = "Button",
            ["Appearance.System"] = "Button",
            ["Appearance.Light"] = "Button",
            ["Appearance.Dark"] = "Button",
        };

        foreach ((string automationId, string expectedType) in criticalTypes)
        {
            XElement element = FindByAutomationId(shell, automationId);
            Assert.Equal(expectedType, element.Name.LocalName);
        }
    }

    [Fact]
    public void LabelsTargetExistingNamedInputs()
    {
        foreach (XamlFile view in LoadViews())
        {
            HashSet<string> names = view.Document
                .Descendants()
                .Select(element => (string?)element.Attribute(XName.Get("Name", XamlNamespace)))
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (XElement label in view.Document.Descendants(XName.Get("Label", PresentationNamespace)))
            {
                string targetBinding = (string?)label.Attribute("Target") ?? string.Empty;
                const string prefix = "{Binding ElementName=";
                Assert.True(
                    targetBinding.StartsWith(prefix, StringComparison.Ordinal) && targetBinding.EndsWith('}'),
                    $"{view.RelativePath}: Label '{(string?)label.Attribute("Content")}' must target a named input.");
                string target = targetBinding[prefix.Length..^1];
                Assert.Contains(target, names);
            }
        }
    }

    [Fact]
    public void ShellNavigationBindsNativeListSelectionToAccessiblePageContent()
    {
        XDocument shell = LoadView("ShellWindow.xaml").Document;
        XElement navigation = FindByAutomationId(shell, "PrimaryNavigation");
        Assert.Equal("{Binding Pages}", (string?)navigation.Attribute("ItemsSource"));
        Assert.Equal("{Binding SelectedPage}", (string?)navigation.Attribute("SelectedItem"));
        Assert.Equal("Name", (string?)navigation.Attribute("DisplayMemberPath"));
        Assert.Equal("Primary navigation", (string?)navigation.Attribute("AutomationProperties.Name"));

        XElement pageScroller = FindByAutomationId(shell, "{Binding SelectedPage.AutomationId}");
        Assert.Equal("ScrollViewer", pageScroller.Name.LocalName);
        Assert.Equal("{Binding SelectedPage.Title}", (string?)pageScroller.Attribute("AutomationProperties.Name"));
        XElement content = Assert.Single(pageScroller.Descendants(XName.Get("ContentControl", PresentationNamespace)));
        Assert.Equal("{Binding SelectedPage}", (string?)content.Attribute("Content"));

        string[] expectedPageTypes =
        [
            "{x:Type presentation:DashboardPageViewModel}",
            "{x:Type presentation:FlightsPageViewModel}",
            "{x:Type presentation:FlightLogPageViewModel}",
            "{x:Type presentation:DiagnosticsPageViewModel}",
            "{x:Type presentation:SettingsPageViewModel}",
            "{x:Type presentation:AboutPageViewModel}",
        ];
        string[] pageTypes = shell
            .Descendants(XName.Get("DataTemplate", PresentationNamespace))
            .Select(static template => (string?)template.Attribute("DataType"))
            .Where(static dataType => dataType is not null && dataType.Contains("PageViewModel", StringComparison.Ordinal))
            .Select(static dataType => dataType!)
            .ToArray();

        foreach (string pageType in expectedPageTypes)
        {
            Assert.Contains(pageType, pageTypes);
        }
    }

    [Fact]
    public void SimulatorSetupKeepsEditionSpecificAccessibleActionsAndLiveStatus()
    {
        XDocument shell = LoadView("ShellWindow.xaml").Document;

        foreach (string edition in new[] { "2020", "2024" })
        {
            foreach (string action in new[] { "Browse", "Path", "Validate", "Test" })
            {
                XElement element = FindByAutomationId(shell, $"SimConnect{edition}.{action}");
                string name = (string?)element.Attribute("AutomationProperties.Name") ?? string.Empty;
                Assert.Contains(edition, name, StringComparison.Ordinal);
            }

            XElement status = Assert.Single(
                shell.Descendants(),
                element =>
                    string.Equals(
                        (string?)element.Attribute("AutomationProperties.Name"),
                        $"MSFS {edition} library status",
                        StringComparison.Ordinal));
            Assert.Equal("Polite", (string?)status.Attribute("AutomationProperties.LiveSetting"));
        }

        Assert.NotEqual(
            (string?)FindByAutomationId(shell, "SimConnect2020.Test").Attribute("AutomationProperties.Name"),
            (string?)FindByAutomationId(shell, "SimConnect2024.Test").Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void FirstRunWizardHasKeyboardNavigationAndAnAccessibleNonBlockingExit()
    {
        XDocument wizard = LoadView("FirstRunWindow.xaml").Document;
        XElement root = wizard.Root ?? throw new InvalidOperationException("FirstRunWindow.xaml has no root element.");
        Assert.Equal("ACARS first-run setup", (string?)root.Attribute("AutomationProperties.Name"));
        Assert.Equal("FirstRun.Window", (string?)root.Attribute("AutomationProperties.AutomationId"));

        IReadOnlyDictionary<string, string> commands = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FirstRun.Later"] = "{Binding SetUpLaterCommand}",
            ["FirstRun.Back"] = "{Binding BackCommand}",
            ["FirstRun.Next"] = "{Binding NextCommand}",
            ["FirstRun.Finish"] = "{Binding FinishCommand}",
        };
        foreach ((string automationId, string command) in commands)
        {
            XElement button = FindByAutomationId(wizard, automationId);
            Assert.Equal("Button", button.Name.LocalName);
            Assert.Equal(command, (string?)button.Attribute("Command"));
        }

        Assert.Equal("True", (string?)FindByAutomationId(wizard, "FirstRun.Next").Attribute("IsDefault"));
        Assert.Equal("True", (string?)FindByAutomationId(wizard, "FirstRun.Finish").Attribute("IsDefault"));

        string[] requiredStepNames =
        [
            "Welcome setup page",
            "phpVMS server setup page",
            "phpVMS credential setup page",
            "phpVMS connection test setup page",
            "Simulator prerequisite setup page",
            "Appearance and accessibility setup page",
            "Ready setup page",
        ];
        string[] actualStepNames = wizard
            .Descendants()
            .Select(static element => (string?)element.Attribute("AutomationProperties.Name"))
            .Where(static name => name is not null && name.EndsWith("setup page", StringComparison.Ordinal))
            .Select(static name => name!)
            .ToArray();
        foreach (string stepName in requiredStepNames)
        {
            Assert.Contains(stepName, actualStepNames);
        }
    }

    [Fact]
    public void ThemeDictionariesExposeTheSameSemanticBrushContract()
    {
        string themesDirectory = RepositoryLayout.FromRoot("src", "ClimbAndMaintain.Acars.App", "Themes");
        foreach (string themeName in new[] { "Theme.Light.xaml", "Theme.Dark.xaml", "Theme.HighContrast.xaml" })
        {
            XDocument theme = XDocument.Load(Path.Combine(themesDirectory, themeName));
            string[] keys = theme
                .Descendants(XName.Get("SolidColorBrush", PresentationNamespace))
                .Select(static brush => (string?)brush.Attribute(XName.Get("Key", XamlNamespace)))
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Select(static key => key!)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();
            string[] expected = SemanticBrushKeys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, keys);

            if (themeName == "Theme.HighContrast.xaml")
            {
                Assert.All(
                    theme.Descendants(XName.Get("SolidColorBrush", PresentationNamespace)),
                    static brush => Assert.Contains(
                        "SystemColors.",
                        (string?)brush.Attribute("Color") ?? string.Empty,
                        StringComparison.Ordinal));
            }
        }

        XDocument tokens = XDocument.Load(Path.Combine(themesDirectory, "Tokens.xaml"));
        string[] nativeStyleTargets = tokens
            .Descendants(XName.Get("Style", PresentationNamespace))
            .Select(static style => (string?)style.Attribute("TargetType"))
            .Where(static target => !string.IsNullOrWhiteSpace(target))
            .Select(static target => target!)
            .ToArray();
        foreach (string target in new[] { "Window", "TextBlock", "Label", "Button", "TextBox", "PasswordBox", "ComboBox", "GroupBox" })
        {
            Assert.Contains(target, nativeStyleTargets);
        }
    }

    [Fact]
    public void AppLoadsSemanticThemeResourcesAndViewsDoNotHardCodeColors()
    {
        XDocument app = XDocument.Load(RepositoryLayout.FromRoot("src", "ClimbAndMaintain.Acars.App", "App.xaml"));
        string[] mergedSources = app
            .Descendants(XName.Get("ResourceDictionary", PresentationNamespace))
            .Select(static dictionary => (string?)dictionary.Attribute("Source"))
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Select(static source => source!)
            .ToArray();
        Assert.Contains("Themes/Theme.Light.xaml", mergedSources);
        Assert.Contains("Themes/Tokens.xaml", mergedSources);

        string[] brushAttributes = ["Background", "BorderBrush", "Foreground"];
        foreach (XamlFile view in LoadViews())
        {
            var hardCodedColors = view.Document
                .Descendants()
                .Attributes()
                .Where(attribute => brushAttributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                .Where(static attribute => attribute.Value.StartsWith('#'))
                .Select(attribute => $"{view.RelativePath}: {attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName}={attribute.Value}")
                .ToArray();
            Assert.Empty(hardCodedColors);
        }
    }

    private static IEnumerable<XamlFile> LoadViews()
    {
        string viewsDirectory = RepositoryLayout.FromRoot("src", "ClimbAndMaintain.Acars.App", "Views");
        return Directory
            .EnumerateFiles(viewsDirectory, "*.xaml", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => new XamlFile(
                Path.GetRelativePath(RepositoryLayout.Root, path).Replace('\\', '/'),
                XDocument.Load(path)));
    }

    private static XamlFile LoadView(string fileName)
    {
        return LoadViews().Single(view => string.Equals(Path.GetFileName(view.RelativePath), fileName, StringComparison.Ordinal));
    }

    private static XElement FindByAutomationId(XDocument document, string automationId)
    {
        return Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute("AutomationProperties.AutomationId"),
                automationId,
                StringComparison.Ordinal));
    }

    private static string ElementIdentity(XElement element)
    {
        string? name = (string?)element.Attribute(XName.Get("Name", XamlNamespace));
        string? content = (string?)element.Attribute("Content");
        return name ?? content ?? "without x:Name or Content";
    }

    private sealed record XamlFile(string RelativePath, XDocument Document);
}
