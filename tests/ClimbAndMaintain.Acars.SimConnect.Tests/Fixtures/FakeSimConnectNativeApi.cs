using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ClimbAndMaintain.Acars.SimConnect.Interop;

namespace ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;

internal sealed class FakeSimConnectNativeApiFactory : ISimConnectNativeApiFactory
{
    private readonly Queue<byte[][]> sessions;
    private readonly List<FakeSimConnectNativeApi> instances = [];

    public FakeSimConnectNativeApiFactory(params byte[][] initialPackets)
        : this([initialPackets])
    {
    }

    private FakeSimConnectNativeApiFactory(IEnumerable<byte[][]> sessions)
    {
        this.sessions = new(sessions);
    }

    public FakeSimConnectNativeApi? Instance { get; private set; }

    public IReadOnlyList<FakeSimConnectNativeApi> Instances => instances;

    public static FakeSimConnectNativeApiFactory ForSessions(params byte[][][] sessions) => new(sessions);

    public ISimConnectNativeApi Load(string absolutePath)
    {
        byte[][] packets = sessions.Count > 0 ? sessions.Dequeue() : [];
        Instance = new(packets)
        {
            AsynchronouslyRejectedDatumName = AsynchronouslyRejectedDatumName,
            SimulatorTime = SimulatorTime,
            IsSlewActive = IsSlewActive,
            PauseState = PauseState,
            TransponderCodeBco16 = TransponderCodeBco16,
        };
        instances.Add(Instance);
        return Instance;
    }

    public string? AsynchronouslyRejectedDatumName { get; init; }

    public DateTimeOffset SimulatorTime { get; init; } = new(2026, 9, 8, 12, 34, 56, TimeSpan.Zero);

    public bool IsSlewActive { get; init; }

    public uint PauseState { get; init; }

    public int TransponderCodeBco16 { get; init; } = 0x1200;
}

internal sealed class FakeSimConnectNativeApi : ISimConnectNativeApi
{
    private readonly ConcurrentQueue<byte[]> packets = new();
    private readonly ConcurrentQueue<(uint DefinitionId, SimConnectPeriod Period)> requests = new();
    private readonly List<int> callingThreadIds = [];
    private readonly List<(uint DefinitionId, string Name, string? Unit)> addedDefinitions = [];
    private readonly Dictionary<uint, int> definitionCounts = [];
    private readonly HashSet<uint> rejectedDefinitionIds = [];
    private readonly object threadIdsGate = new();
    private EventWaitHandle? dispatchEvent;
    private nint dispatchMemory;
    private uint lastSentPacketId;
    private bool disposed;

    public FakeSimConnectNativeApi(IEnumerable<byte[]> initialPackets)
    {
        foreach (byte[] packet in initialPackets)
        {
            packets.Enqueue(packet);
        }
    }

    public IReadOnlyList<int> CallingThreadIds
    {
        get
        {
            lock (threadIdsGate)
            {
                return callingThreadIds.ToArray();
            }
        }
    }

    public IReadOnlyList<(uint DefinitionId, string Name, string? Unit)> AddedDefinitions => addedDefinitions;

    public IReadOnlyList<(uint DefinitionId, SimConnectPeriod Period)> Requests => requests.ToArray();

    public string? AsynchronouslyRejectedDatumName { get; init; }

    public DateTimeOffset SimulatorTime { get; init; }

    public bool IsSlewActive { get; init; }

    public uint PauseState { get; init; }

    public int TransponderCodeBco16 { get; init; }

    public int Open(out nint connection, string clientName, EventWaitHandle dispatchEvent, uint configIndex)
    {
        RecordThread();
        this.dispatchEvent = dispatchEvent;
        connection = 42;
        dispatchEvent.Set();
        return 0;
    }

    public int Close(nint connection)
    {
        RecordThread();
        return 0;
    }

    public int GetNextDispatch(nint connection, out nint data, out uint dataSize)
    {
        RecordThread();
        FreeDispatchMemory();
        if (!packets.TryDequeue(out byte[]? packet))
        {
            data = 0;
            dataSize = 0;
            return 0;
        }

        if (packet.Length == 0)
        {
            data = 0;
            dataSize = 0;
            return SimConnectConstants.StatusRemoteDisconnect;
        }

        dispatchMemory = Marshal.AllocHGlobal(packet.Length);
        Marshal.Copy(packet, 0, dispatchMemory, packet.Length);
        data = dispatchMemory;
        dataSize = checked((uint)packet.Length);
        return 0;
    }

    public int AddToDataDefinition(
        nint connection,
        uint definitionId,
        string datumName,
        string? unitsName,
        SimConnectDataType dataType,
        float epsilon,
        uint datumId)
    {
        RecordThread();
        uint packetId = ++lastSentPacketId;
        addedDefinitions.Add((definitionId, datumName, unitsName));
        definitionCounts.TryGetValue(definitionId, out int count);
        definitionCounts[definitionId] = count + 1;
        if (string.Equals(datumName, AsynchronouslyRejectedDatumName, StringComparison.Ordinal))
        {
            rejectedDefinitionIds.Add(definitionId);
            packets.Enqueue(PacketFixtures.Exception(SimConnectExceptionCode.UnrecognizedId, packetId));
            dispatchEvent!.Set();
        }

        return 0;
    }

    public int ClearDataDefinition(nint connection, uint definitionId)
    {
        RecordThread();
        return 0;
    }

    public int RequestDataOnSimObject(
        nint connection,
        uint requestId,
        uint definitionId,
        uint objectId,
        SimConnectPeriod period,
        uint flags,
        uint origin,
        uint interval,
        uint limit)
    {
        RecordThread();
        ++lastSentPacketId;
        requests.Enqueue((definitionId, period));
        if (rejectedDefinitionIds.Contains(definitionId))
        {
            return 0;
        }

        byte[] response = definitionId switch
        {
            0x434D1003 => PacketFixtures.StandardSystems(requestId, definitionId),
            0x434D1005 => PacketFixtures.AircraftIdentity(requestId, definitionId),
            0x434D1101 => PacketFixtures.SimulatorTime(requestId, definitionId, SimulatorTime),
            0x434D1103 => PacketFixtures.DynamicValues(requestId, definitionId, IsSlewActive ? 1 : 0),
            0x434D1105 => PacketFixtures.DynamicValues(requestId, definitionId, TransponderCodeBco16),
            >= 0x434D2000 and < 0x434D2100 => PacketFixtures.DynamicValues(
                requestId,
                definitionId,
                [definitionId - 0x434D2000 + 1]),
            0x434D1009 => PacketFixtures.FlightControls(requestId, definitionId),
            _ => PacketFixtures.CoreTelemetry(requestId, definitionId),
        };
        packets.Enqueue(response);
        dispatchEvent!.Set();
        return 0;
    }

    public int SubscribeToSystemEvent(nint connection, uint eventId, string eventName)
    {
        RecordThread();
        ++lastSentPacketId;
        if (string.Equals(eventName, "Pause", StringComparison.Ordinal))
        {
            packets.Enqueue(PacketFixtures.Event(eventId, PauseState));
            dispatchEvent!.Set();
        }

        return 0;
    }

    public int UnsubscribeFromSystemEvent(nint connection, uint eventId)
    {
        RecordThread();
        return 0;
    }

    public int RequestSystemState(nint connection, uint requestId, string stateName)
    {
        RecordThread();
        return 0;
    }

    public int GetLastSentPacketId(nint connection, out uint packetId)
    {
        RecordThread();
        packetId = lastSentPacketId;
        return 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        RecordThread();
        disposed = true;
        FreeDispatchMemory();
    }

    private void RecordThread()
    {
        lock (threadIdsGate)
        {
            callingThreadIds.Add(Environment.CurrentManagedThreadId);
        }
    }

    private void FreeDispatchMemory()
    {
        if (dispatchMemory == 0)
        {
            return;
        }

        Marshal.FreeHGlobal(dispatchMemory);
        dispatchMemory = 0;
    }
}
