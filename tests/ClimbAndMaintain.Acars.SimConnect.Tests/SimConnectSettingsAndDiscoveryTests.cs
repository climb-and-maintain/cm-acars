using ClimbAndMaintain.Acars.SimConnect.Configuration;
using ClimbAndMaintain.Acars.SimConnect.Discovery;
using ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Tests;

public sealed class SimConnectSettingsAndDiscoveryTests
{
    [Fact]
    public void SelectionsAreIndependentPerSimulator()
    {
        SimConnectLibrarySettings settings = new SimConnectLibrarySettings()
            .WithSelection(SimConnectTarget.Msfs2020, @"C:\SDK2020\SimConnect.dll")
            .WithSelection(SimConnectTarget.Msfs2024, @"C:\SDK2024\SimConnect.dll");

        Assert.Equal(@"C:\SDK2020\SimConnect.dll", settings.Msfs2020.Path);
        Assert.Equal(@"C:\SDK2024\SimConnect.dll", settings.Msfs2024.Path);
        Assert.Null(settings.Msfs2020.Attestation);
        Assert.Null(settings.Msfs2024.Attestation);
    }

    [Fact]
    public void SelectingNewPathInvalidatesOnlyThatTargetsAttestation()
    {
        SimConnectLibraryAttestation attestation2020 = CreateAttestation(SimConnectTarget.Msfs2020, @"C:\2020\SimConnect.dll", "AA");
        SimConnectLibraryAttestation attestation2024 = CreateAttestation(SimConnectTarget.Msfs2024, @"C:\2024\SimConnect.dll", "BB");
        SimConnectLibrarySettings settings = new SimConnectLibrarySettings()
            .WithAttestation(attestation2020)
            .WithAttestation(attestation2024)
            .WithSelection(SimConnectTarget.Msfs2020, @"C:\new\SimConnect.dll");

        Assert.Null(settings.Msfs2020.Attestation);
        Assert.Same(attestation2024, settings.Msfs2024.Attestation);
    }

    [Fact]
    public void FileHashChangeInvalidatesAttestation()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        SimConnectLibraryAttestation attestation = CreateAttestation(
            SimConnectTarget.Msfs2024,
            "/sdk/SimConnect.dll",
            "OLD",
            timestamp);
        SimConnectPeMetadata metadata = new(
            "/sdk/SimConnect.dll",
            "NEW",
            123,
            timestamp,
            0x8664,
            true,
            false,
            SimConnectExportSurface.RequiredExports);

        Assert.False(attestation.Matches(metadata));
    }

    [Fact]
    public void DiscoveryChecksOnlyKnownRelativePathsUnderAdditionalRoot()
    {
        using TemporaryFile file = new(PortableExecutableFixture.Create());
        SimConnectLibraryDiscovery discovery = new(new SimConnectLibraryValidator());

        IReadOnlyList<SimConnectLibraryCandidate> candidates = discovery.Discover(
            new(),
            [file.DirectoryPath]);

        SimConnectLibraryCandidate candidate = Assert.Single(candidates);
        Assert.Equal(file.Path, candidate.Path);
        Assert.True(candidate.Validation.IsCompatible);
    }

    [Fact]
    public void CoreConstantsMatchNativeAbi()
    {
        Assert.Equal(2U, (uint)SimConnectReceiveId.Open);
        Assert.Equal(8U, (uint)SimConnectReceiveId.SimObjectData);
        Assert.Equal(15U, (uint)SimConnectReceiveId.SystemState);
        Assert.Equal(4U, (uint)SimConnectDataType.Float64);
        Assert.Equal(4U, (uint)SimConnectPeriod.Second);
        Assert.Equal(uint.MaxValue, SimConnectConstants.OpenConfigIndexLocal);
        Assert.True(SimConnectConstants.Succeeded(1));
        Assert.True(SimConnectConstants.Failed(unchecked((int)0x80004005)));
    }

    private static SimConnectLibraryAttestation CreateAttestation(
        SimConnectTarget target,
        string path,
        string hash,
        DateTimeOffset? timestamp = null) => new()
        {
            Target = target,
            CanonicalPath = path,
            Sha256 = hash,
            FileLength = 123,
            LastWriteTimeUtc = timestamp ?? DateTimeOffset.UnixEpoch,
            Server = new("Simulator", target == SimConnectTarget.Msfs2020 ? 11U : 12U, 0, 0, 0, 0, 0, 0, 0),
            Capabilities = SimConnectCapabilities.NativeCoreApi,
            TestedAtUtc = DateTimeOffset.UnixEpoch,
        };
}
