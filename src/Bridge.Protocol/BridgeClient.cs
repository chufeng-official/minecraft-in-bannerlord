using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BannerlordBlocks.Bridge
{
    // No engine references or callbacks. The mission drains events on its own thread.
    public sealed class BridgeEvent
    {
        public Message Message;
        public string Status;
    }
    public sealed class BridgeClient : IDisposable
    {
        private readonly ConcurrentQueue<Message> outgoing = new ConcurrentQueue<Message>();
        private readonly object sendGate = new object();
        private readonly ConcurrentQueue<BridgeEvent> incoming = new ConcurrentQueue<BridgeEvent>();
        private readonly string session, scene;
        private readonly int port;
        private volatile bool stopped;
        private volatile bool connected;
        private volatile TcpClient socket;
        public BridgeClient(string session, string scene, int port)
        {
            this.session = session; this.scene = scene; this.port = port;
            new Thread(Run) { IsBackground = true, Name = "BannerlordBlocks bridge" }.Start();
        }
        public bool Send(Message message)
        {
            lock (sendGate)
            {
                if (stopped || !connected || outgoing.Count >= 32) return false;
                outgoing.Enqueue(message); return true;
            }
        }
        public bool TryRead(out BridgeEvent item) { return incoming.TryDequeue(out item); }
        private void Publish(BridgeEvent item)
        {
            if (incoming.Count >= 128) throw new IOException("Inbound queue budget exceeded");
            incoming.Enqueue(item);
        }
        private void Run()
        {
            while (!stopped)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        socket = client;
                        var connect = client.ConnectAsync(IPAddress.Loopback, port);
                        if (!connect.Wait(1000)) throw new IOException("Connect timeout");
                        client.NoDelay = true; client.ReceiveTimeout = 3000; client.SendTimeout = 3000;
                        NetworkStream stream = client.GetStream();
                        Wire.Write(stream, Message.For("Hello", session, scene));
                        Message hello = Wire.Read(stream);
                        if (hello.Type != "Hello" || hello.Session != session || hello.Scene != scene)
                            throw new InvalidDataException("Invalid handshake");
                        Wire.Write(stream, Message.For("SceneBegin", session, scene));
                        // MC snapshots can be assembled over multiple 20 Hz server ticks.
                        client.ReceiveTimeout = 30000;
                        Message snapshot = Wire.Read(stream);
                        if (snapshot.Type != "Snapshot" || snapshot.Session != session || snapshot.Scene != scene)
                            throw new InvalidDataException("Initial snapshot required");
                        client.ReceiveTimeout = 3000;
                        connected = true;
                        Publish(new BridgeEvent { Message = snapshot });
                        DateTime lastReceive = DateTime.UtcNow, heartbeat = DateTime.MinValue;
                        while (!stopped)
                        {
                            Message send;
                            for (int i = 0; i < 16 && outgoing.TryDequeue(out send); i++) Wire.Write(stream, send);
                            if ((DateTime.UtcNow - heartbeat).TotalSeconds >= 1)
                            { Wire.Write(stream, Message.For("Heartbeat", session, scene)); heartbeat = DateTime.UtcNow; }
                            if (stream.DataAvailable)
                            {
                                Message received = Wire.Read(stream);
                                if (received.Session != session || received.Scene != scene) throw new InvalidDataException("Stale connection message");
                                lastReceive = DateTime.UtcNow;
                                if (received.Type != "Heartbeat") Publish(new BridgeEvent { Message = received });
                            }
                            else if ((DateTime.UtcNow - lastReceive).TotalSeconds > 5) throw new IOException("Heartbeat timeout");
                            else Thread.Sleep(10);
                        }
                        Wire.Write(stream, Message.For("SceneEnd", session, scene));
                    }
                }
                catch (Exception ex)
                {
                    lock (sendGate)
                    {
                        connected = false;
                        Message ignored; while (outgoing.TryDequeue(out ignored)) { }
                    }
                    // A disconnect marker must follow previously queued deltas, even after overflow.
                    while (incoming.Count >= 128) { BridgeEvent old; incoming.TryDequeue(out old); }
                    incoming.Enqueue(new BridgeEvent { Status = "Disconnected: " + ex.GetType().Name });
                }
                finally { connected = false; socket = null; }
                for (int i = 0; i < 10 && !stopped; i++) Thread.Sleep(100);
            }
        }
        public void Dispose()
        {
            stopped = true;
            // The connected worker sends SceneEnd and closes asynchronously. Reads/writes have
            // finite timeouts; an incomplete handshake can be cancelled immediately.
            if (!connected)
                try { socket?.Close(); } catch (SocketException) { }
            connected = false;
        }
    }
}
