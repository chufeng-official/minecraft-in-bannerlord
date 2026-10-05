using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BannerlordBlocks.Bridge;

int port = args.Length == 0 ? 25575 : int.Parse(args[0]);
if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
Console.WriteLine($"M2 mock authority listening on 127.0.0.1:{port}; Ctrl+C to stop. No Minecraft required.");
World world = null;
// Exactly one active host; replacing a scene drops old state. Disconnection retains it for reconnect.
while (true)
{
    using var client = listener.AcceptTcpClient();
    client.NoDelay = true; client.ReceiveTimeout = 10000; client.SendTimeout = 3000;
    try
    {
        using var stream = client.GetStream();
        Message hello = Wire.Read(stream);
        if (hello.Type != "Hello") throw new InvalidDataException("Hello required");
        Wire.Write(stream, Message.For("Hello", hello.Session, hello.Scene));
        Message begin = Wire.Read(stream);
        if (begin.Type != "SceneBegin" || begin.Session != hello.Session || begin.Scene != hello.Scene)
            throw new InvalidDataException("SceneBegin required");
        if (world == null || world.Session != begin.Session || world.Scene != begin.Scene)
            world = new World(begin.Session, begin.Scene);
        Wire.Write(stream, world.Snapshot());
        Console.WriteLine($"Scene {begin.Scene}, session {begin.Session}, sequence {world.Sequence}");
        while (true)
        {
            Message request = Wire.Read(stream);
            if (request.Session != world.Session || request.Scene != world.Scene) throw new InvalidDataException("Stale scene");
            if (request.Type == "SceneEnd") { world = null; break; }
            foreach (Message response in world.Handle(request)) Wire.Write(stream, response);
        }
    }
    catch (Exception ex) when (ex is IOException || ex is SocketException || ex is System.Runtime.Serialization.SerializationException)
    { Console.WriteLine($"Connection closed: {ex.GetType().Name}: {ex.Message}"); }
}
