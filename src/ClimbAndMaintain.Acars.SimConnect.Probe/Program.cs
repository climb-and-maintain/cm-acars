using System.Text.Json;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Probe;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: ClimbAndMaintain.Acars.SimConnect.Probe <absolute-library-path>");
            return 64;
        }

        SimConnectRuntimeProbe probe = new(new SimConnectLibraryValidator());
        SimConnectRuntimeProbeResult result = probe.Probe(args[0]);
        Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
        return result.Succeeded ? 0 : 1;
    }

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
    };
}
