using System;
using System.Collections.Generic;
using System.IO;

namespace BannerlordBlocks.Bridge
{
    // One authority per active scene. Accessed by the server's single connection loop.
    public sealed class World
    {
        public readonly string Session, Scene;
        private readonly Dictionary<string, Cell> blocks = new Dictionary<string, Cell>();
        private readonly Dictionary<string, Message[]> results = new Dictionary<string, Message[]>();
        private readonly Queue<string> requestOrder = new Queue<string>();
        public long Sequence { get; private set; }
        public double? BaseHeight { get; private set; }
        public World(string session, string scene) { Session = session; Scene = scene; }
        public Message Snapshot()
        {
            var cells = new Cell[blocks.Count]; blocks.Values.CopyTo(cells, 0);
            return new Message { Type = "Snapshot", Session = Session, Scene = Scene, Sequence = Sequence, BaseHeight = BaseHeight, Blocks = cells };
        }
        public Message[] Handle(Message request)
        {
            request.Validate();
            if (request.Session != Session || request.Scene != Scene) throw new InvalidDataException("Stale scene");
            if (request.Type == "SnapshotRequest" || request.Type == "SceneBegin") return new[] { Snapshot() };
            if (request.Type == "Heartbeat") return new[] { Message.For("Heartbeat", Session, Scene) };
            if (request.Type != "PlaceBlockRequest" && request.Type != "BreakBlockRequest" && request.Type != "ClearRequest")
                throw new InvalidDataException("Unexpected world message");
            Message[] cached;
            if (results.TryGetValue(request.Request, out cached)) return cached;
            string error = null;
            Message change = null;
            if (request.Type == "PlaceBlockRequest")
            {
                if (BaseHeight.HasValue && Math.Abs(BaseHeight.Value - request.BaseHeight.Value) > 0.001) error = "origin mismatch";
                else if (blocks.ContainsKey(request.Cell.Key)) error = "occupied";
                else if (blocks.Count >= Wire.MaxSnapshotBlocks) error = "M2 snapshot safety budget exceeded";
                else
                {
                    BaseHeight = BaseHeight ?? request.BaseHeight;
                    blocks.Add(request.Cell.Key, request.Cell);
                    change = Delta(request, true);
                }
            }
            else if (request.Type == "BreakBlockRequest")
            {
                if (!blocks.Remove(request.Cell.Key)) error = "not found";
                else change = Delta(request, false);
            }
            else
            {
                blocks.Clear(); Sequence++;
                change = Snapshot(); // Keep the scene's origin, as M1 does.
            }
            var result = Message.For("Result", Session, Scene);
            result.Request = request.Request; result.Sequence = Sequence; result.Error = error;
            var response = change == null ? new[] { result } : new[] { change, result };
            results.Add(request.Request, response); requestOrder.Enqueue(request.Request);
            // Bounded deduplication window; clients never automatically replay unknown old requests.
            if (requestOrder.Count > 1024) results.Remove(requestOrder.Dequeue());
            return response;
        }
        private Message Delta(Message request, bool present)
        {
            return new Message { Type = "BlockDelta", Session = Session, Scene = Scene, Request = request.Request,
                Cell = request.Cell, Present = present, Sequence = ++Sequence, BaseHeight = BaseHeight };
        }
    }

    public sealed class Replica
    {
        public readonly Dictionary<string, Cell> Blocks = new Dictionary<string, Cell>();
        public long Sequence { get; private set; }
        public double? BaseHeight { get; private set; }
        public bool Ready { get; private set; }
        private readonly string session, scene;
        public Replica(string session, string scene) { this.session = session; this.scene = scene; }
        public void Disconnect() { Ready = false; }
        public bool Apply(Message message)
        {
            message.Validate();
            if (message.Session != session || message.Scene != scene) return false;
            if (message.Type == "Snapshot")
            {
                if (Ready && message.Sequence < Sequence) return false;
                // A fresh snapshot after service restart may legitimately reset sequence to zero.
                var cells = new Dictionary<string, Cell>();
                foreach (Cell cell in message.Blocks)
                    if (cells.ContainsKey(cell.Key)) throw new InvalidDataException("Duplicate snapshot cell");
                    else cells.Add(cell.Key, cell);
                Blocks.Clear(); foreach (var pair in cells) Blocks.Add(pair.Key, pair.Value);
                Sequence = message.Sequence; BaseHeight = message.BaseHeight; Ready = true; return true;
            }
            if (message.Type != "BlockDelta" || !Ready) return false;
            if (message.Sequence <= Sequence) return false;
            if (message.Sequence != Sequence + 1 || !BaseHeight.HasValue && !message.Present ||
                BaseHeight.HasValue && Math.Abs(BaseHeight.Value - message.BaseHeight.Value) > 0.001)
            { Ready = false; return false; }
            BaseHeight = message.BaseHeight;
            if (message.Present) Blocks[message.Cell.Key] = message.Cell;
            else Blocks.Remove(message.Cell.Key);
            Sequence = message.Sequence; return true;
        }
    }
}
