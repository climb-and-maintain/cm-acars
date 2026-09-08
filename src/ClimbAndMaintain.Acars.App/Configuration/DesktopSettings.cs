using ClimbAndMaintain.Acars.SimConnect.Configuration;

namespace ClimbAndMaintain.Acars.App.Configuration;

public enum AppearanceMode
{
    System,
    Light,
    Dark,
}

public sealed record DesktopSettings
{
    public bool SetupCompleted { get; init; }

    public string PhpVmsBaseUrl { get; init; } = "https://";

    public bool AllowInsecureLocalServer { get; init; }

    public AppearanceMode Appearance { get; init; } = AppearanceMode.System;

    public SimConnectLibrarySettings SimConnect { get; init; } = new();
}
