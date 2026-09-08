using ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Tests;

public sealed class SimConnectLibraryValidatorTests
{
    private readonly SimConnectLibraryValidator validator = new();

    [Fact]
    public void RequiresAnAbsolutePath()
    {
        SimConnectLibraryValidationResult result = validator.Validate("SimConnect.dll");

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.PathMustBeAbsolute);
    }

    [Fact]
    public void ReportsMissingFile()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll");

        SimConnectLibraryValidationResult result = validator.Validate(path);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.FileNotFound);
    }

    [Fact]
    public void AcceptsCraftedNativeX64PeWithRequiredExports()
    {
        using TemporaryFile file = new(PortableExecutableFixture.Create());

        SimConnectLibraryValidationResult result = validator.Validate(file.Path);

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Metadata);
        Assert.Equal(0x8664, result.Metadata.Machine);
        Assert.True(result.Metadata.IsPe32Plus);
        Assert.False(result.Metadata.HasClrHeader);
        Assert.Equal(64, result.Metadata.Sha256.Length);
        Assert.All(SimConnectExportSurface.RequiredExports, name => Assert.Contains(name, result.Metadata.ExportNames));
    }

    [Fact]
    public void LoadLeaseRevalidatesAndDeniesWritesUntilDisposed()
    {
        using TemporaryFile file = new(PortableExecutableFixture.Create());
        SimConnectLibraryValidationResult initial = validator.Validate(file.Path);

        using (SimConnectLibraryLoadLease lease = validator.AcquireLoadLease(initial))
        {
            Assert.True(lease.Validation.IsCompatible);
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            Assert.Throws<IOException>(() => File.Open(
                file.Path,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read));
        }

        using FileStream writable = File.Open(
            file.Path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read);
        Assert.True(writable.CanWrite);
    }

    [Fact]
    public void RejectsWrongArchitecture()
    {
        using TemporaryFile file = new(PortableExecutableFixture.Create(machine: 0x014C));

        SimConnectLibraryValidationResult result = validator.Validate(file.Path);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.WrongArchitecture);
    }

    [Fact]
    public void RejectsManagedWrapperWithSpecificGuidance()
    {
        using TemporaryFile file = new(
            PortableExecutableFixture.Create(managed: true),
            "Microsoft.FlightSimulator.SimConnect.dll");

        SimConnectLibraryValidationResult result = validator.Validate(file.Path);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == SimConnectDiagnosticCodes.ManagedWrapperSelected
            && item.Message.Contains("managed wrapper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReportsEveryMissingRequiredExport()
    {
        using TemporaryFile file = new(PortableExecutableFixture.Create(["SimConnect_Open", "SimConnect_Close"]));

        SimConnectLibraryValidationResult result = validator.Validate(file.Path);

        int expectedMissing = SimConnectExportSurface.RequiredExports.Count - 2;
        Assert.False(result.IsCompatible);
        Assert.Equal(expectedMissing, result.Diagnostics.Count(item => item.Code == SimConnectDiagnosticCodes.MissingExport));
    }

    [Fact]
    public void RejectsArbitraryNonPeData()
    {
        using TemporaryFile file = new([1, 2, 3, 4]);

        SimConnectLibraryValidationResult result = validator.Validate(file.Path);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.InvalidPortableExecutable);
    }

    [Fact]
    public void RejectsOversizedLibraryBeforeReadingItIntoMemory()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"oversized-{Guid.NewGuid():N}.dll");
        try
        {
            using (FileStream stream = File.Create(path))
            {
                stream.SetLength((64L * 1024 * 1024) + 1);
            }

            SimConnectLibraryValidationResult result = validator.Validate(path);

            Assert.False(result.IsCompatible);
            Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.FileReadFailed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RuntimeProbeKeepsStaticValidationAvailableOffWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryFile file = new(PortableExecutableFixture.Create());
        SimConnectRuntimeProbe probe = new(validator);

        SimConnectRuntimeProbeResult result = probe.Probe(file.Path);

        Assert.True(result.StaticValidation.IsCompatible);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == SimConnectDiagnosticCodes.PlatformUnsupported);
    }
}
