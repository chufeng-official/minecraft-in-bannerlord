using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BannerlordBlocks.Bridge;

// Interop fixtures use actual DataContractJsonSerializer bytes; never commit generated frames.
if (args.Length == 2 && args[0] == "--write-fixtures")
{
    System.IO.Directory.CreateDirectory(args[1]);
    string fixtureSession = "1234567890abcdef1234567890abcdef", fixtureScene = "bridge_test";
    var fixturePlace = Message.For("PlaceBlockRequest", fixtureSession, fixtureScene);
    fixturePlace.Request = "abcdef1234567890abcdef1234567890";
    fixturePlace.Cell = new Cell { X = -120, Y = 1029, Z = -2 }; fixturePlace.BaseHeight = 12.4476;
    using (var output = File.Create(Path.Combine(args[1], "csharp-place.frame"))) Wire.Write(output, fixturePlace);
    var fixtureSnapshot = Message.For("Snapshot", fixtureSession, fixtureScene);
    fixtureSnapshot.Sequence = 2; fixtureSnapshot.BaseHeight = 12.4476; fixtureSnapshot.Blocks = new[] { fixturePlace.Cell };
    using (var output = File.Create(Path.Combine(args[1], "csharp-snapshot.frame"))) Wire.Write(output, fixtureSnapshot);
    Console.WriteLine("PASS: C# interop fixtures generated.");
    return;
}
if (args.Length == 2 && args[0] == "--verify-java-fixture")
{
    using var input = File.OpenRead(Path.Combine(args[1], "java-delta.frame"));
    var delta = Wire.Read(input);
    if (delta.Type != "BlockDelta" || delta.Session != "1234567890abcdef1234567890abcdef" || delta.Scene != "bridge_test" ||
        delta.Cell.X != -120 || delta.Cell.Y != 1029 || delta.Cell.Z != -2 || delta.BaseHeight != 12.4476 || delta.Sequence != 1 || !delta.Present)
        throw new Exception("Java -> C# interop fixture mismatch");
    Console.WriteLine("PASS: Java -> C# framed JSON interop.");
    return;
}

int checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new Exception("FAIL: " + name); Interlocked.Increment(ref checks); }
void Throws(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException || ex is System.Runtime.Serialization.SerializationException || ex is EndOfStreamException)
    { checks++; return; }
    throw new Exception("FAIL: " + name);
}
string session = Guid.NewGuid().ToString("N"), scene = "test_scene";
Message Request(string type, int x = -2, int y = 3, int z = 0)
{
    var m = Message.For(type, session, scene);
    m.Request = Guid.NewGuid().ToString("N");
    m.Cell = new Cell { X = x, Y = y, Z = z }; m.BaseHeight = 12.4476;
    return m;
}
var world = new World(session, scene);
var replica = new Replica(session, scene);
Check(replica.Apply(world.Snapshot()) && replica.Ready, "initial empty snapshot");
Message place = Request("PlaceBlockRequest");
Message[] placed = world.Handle(place);
Check(placed.Length == 2 && placed[0].Present && placed[0].Sequence == 1, "authoritative place delta");
Check(replica.Apply(placed[0]) && replica.Blocks.Count == 1, "mirror place");
Check(!replica.Apply(placed[0]) && replica.Sequence == 1, "duplicate delta ignored");
Check(world.Handle(place)[0].Sequence == 1 && world.Sequence == 1, "request deduplication");
Check(world.Handle(Request("PlaceBlockRequest"))[0].Error == "occupied", "occupied rejection");
var wrongOrigin = Request("PlaceBlockRequest", 9); wrongOrigin.BaseHeight = 100;
Check(world.Handle(wrongOrigin)[0].Error == "origin mismatch", "origin mismatch rejection");
Message stale = Request("BreakBlockRequest"); stale.Session = Guid.NewGuid().ToString("N");
Throws(() => world.Handle(stale), "stale scene authority rejection");
Check(!replica.Apply(new World(stale.Session, scene).Snapshot()) && replica.Blocks.Count == 1, "late prior scene ignored");
Message gap = world.Handle(Request("PlaceBlockRequest", 8))[0];
world.Handle(Request("PlaceBlockRequest", 9));
Message skipped = world.Handle(Request("PlaceBlockRequest", 10))[0];
Check(!replica.Apply(skipped) && !replica.Ready, "sequence gap disables interaction");
Check(replica.Apply(world.Snapshot()) && replica.Blocks.Count == 4 && replica.Sequence == 4, "snapshot recovers gap");
Check(!replica.Apply(new World(session, scene).Snapshot()) && replica.Sequence == 4, "stale snapshot ignored while connected");
Check(replica.Apply(world.Handle(Request("BreakBlockRequest"))[0]) && replica.Blocks.Count == 3, "authoritative break");
Check(world.Handle(Request("BreakBlockRequest"))[0].Error == "not found", "missing block rejection");
Check(replica.Apply(world.Handle(Request("ClearRequest"))[0]) && replica.Blocks.Count == 0 && replica.BaseHeight.HasValue, "clear preserves origin");
replica.Disconnect();
Check(replica.Apply(new World(session, scene).Snapshot()) && replica.Sequence == 0 && replica.BaseHeight == null, "service restart resets state");

using (var bytes = new MemoryStream())
{
    Wire.Write(bytes, placed[0]); bytes.Position = 0;
    using var fragmented = new FragmentedStream(bytes);
    var received = Wire.Read(fragmented);
    Check(received.Cell.X == -2 && received.BaseHeight == 12.4476, "fragmented frame and negative coordinates roundtrip");
}
using (var bytes = new MemoryStream())
{
    Wire.Write(bytes, placed[0]); Wire.Write(bytes, placed[1]); bytes.Position = 0;
    Check(Wire.Read(bytes).Type == "BlockDelta" && Wire.Read(bytes).Type == "Result" && bytes.Position == bytes.Length,
        "coalesced frames preserve message boundaries");
}
Throws(() => Wire.Read(new MemoryStream(new byte[] { 0, 0, 0, 0 })), "zero frame rejected");
Throws(() => Wire.Read(new MemoryStream(new byte[] { 0x7f, 0xff, 0xff, 0xff })), "oversized frame rejected before allocation");
Throws(() => Wire.Read(new MemoryStream(new byte[] { 0, 0, 0, 10, 1 })), "truncated frame rejected");
var invalid = Request("PlaceBlockRequest"); invalid.BaseHeight = double.NaN;
Throws(() => invalid.Validate(), "NaN rejected");
invalid = Request("PlaceBlockRequest", int.MinValue);
Throws(() => invalid.Validate(), "coordinate overflow rejected");
invalid = Request("PlaceBlockRequest"); invalid.Version = 2;
Throws(() => invalid.Validate(), "version mismatch rejected");
invalid = world.Snapshot(); invalid.Blocks = new Cell[Wire.MaxSnapshotBlocks + 1];
Throws(() => invalid.Validate(), "snapshot safety budget enforced");
invalid = world.Snapshot(); invalid.Blocks = new[] { place.Cell, place.Cell }; invalid.BaseHeight = 12.4476;
Throws(() => replica.Apply(invalid), "duplicate snapshot rejected atomically");
Check(replica.Blocks.Count == 0, "invalid snapshot leaves old mirror untouched");

// Real loopback socket test: fragment reads, request/ack, forced disconnect, then automatic reconnect.
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var authority = new World(session, scene);
var server = Task.Run(async () =>
{
    for (int connection = 0; connection < 2; connection++)
    {
        using var peer = await listener.AcceptTcpClientAsync(stop.Token);
        peer.ReceiveTimeout = 5000; peer.SendTimeout = 5000;
        using var stream = peer.GetStream();
        Message hello = Wire.Read(stream);
        Wire.Write(stream, Message.For("Hello", hello.Session, hello.Scene));
        Check(Wire.Read(stream).Type == "SceneBegin", "TCP scene begin");
        Wire.Write(stream, authority.Snapshot());
        if (connection == 1)
        {
            while (true)
            {
                Message end = Wire.Read(stream);
                if (end.Type == "SceneEnd") { Check(end.Session == session, "asynchronous scene end"); break; }
                foreach (Message response in authority.Handle(end)) Wire.Write(stream, response);
            }
            break;
        }
        while (true)
        {
            Message request = Wire.Read(stream);
            foreach (Message response in authority.Handle(request)) Wire.Write(stream, response);
            if (request.Type == "PlaceBlockRequest") break; // Deliberate connection drop.
        }
    }
});
using (var client = new BridgeClient(session, scene, port))
{
    BridgeEvent WaitFor(Func<BridgeEvent, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (client.TryRead(out var item) && predicate(item)) return item;
            Thread.Sleep(10);
        }
        throw new Exception("TCP event timeout");
    }
    Check(WaitFor(e => e.Message?.Type == "Snapshot").Message.Blocks.Length == 0, "TCP initial snapshot");
    Message tcpPlace = Request("PlaceBlockRequest", -15, -20, 2);
    // Retry briefly if the connection closes just as the main thread submits a request.
    DateTime sendDeadline = DateTime.UtcNow.AddSeconds(2);
    while (!client.Send(tcpPlace))
    { if (DateTime.UtcNow > sendDeadline) throw new Exception("send timeout"); Thread.Sleep(1); }
    Check(WaitFor(e => e.Message?.Type == "BlockDelta").Message.Cell.X == -15, "TCP authority delta");
    Check(WaitFor(e => e.Status != null).Status.StartsWith("Disconnected:"), "TCP disconnect event");
    Check(WaitFor(e => e.Message?.Type == "Snapshot").Message.Blocks.Length == 1, "TCP reconnect preserves authoritative world");
}
server.GetAwaiter().GetResult(); listener.Stop(); stop.Dispose();
Console.WriteLine($"PASS: {checks} M2 protocol, authority, replica and loopback checks.");

sealed class FragmentedStream : Stream
{
    private readonly Stream inner;
    public FragmentedStream(Stream inner) { this.inner = inner; }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(1, count));
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
