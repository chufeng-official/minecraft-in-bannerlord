using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BannerlordBlocks.Bridge
{
    [DataContract]
    public sealed class Cell
    {
        [DataMember] public int X;
        [DataMember] public int Y;
        [DataMember] public int Z;
        public string Key { get { return X + ":" + Y + ":" + Z; } }
        public void Validate()
        {
            if (Math.Abs((long)X) > 100000 || Math.Abs((long)Y) > 100000 || Math.Abs((long)Z) > 100000)
                throw new InvalidDataException("Cell out of range");
        }
    }

    [DataContract]
    public sealed class Message
    {
        [DataMember] public int Version = 1;
        [DataMember] public string Type;
        [DataMember] public string Session;
        [DataMember] public string Scene;
        [DataMember] public string Request;
        [DataMember] public long Sequence;
        [DataMember] public Cell Cell;
        [DataMember] public Cell[] Blocks;
        [DataMember] public double? BaseHeight;
        [DataMember] public bool Present;
        [DataMember] public string Error;

        public static Message For(string type, string session, string scene)
        { return new Message { Type = type, Session = session, Scene = scene }; }

        public void Validate()
        {
            if (Version != 1) throw new InvalidDataException("Unsupported protocol version");
            switch (Type)
            {
                case "Hello": case "SceneBegin": case "SceneEnd": case "SnapshotRequest":
                case "Snapshot": case "PlaceBlockRequest": case "BreakBlockRequest":
                case "ClearRequest": case "BlockDelta": case "Result": case "Heartbeat": break;
                default: throw new InvalidDataException("Unknown message type");
            }
            Guid id;
            if (!Guid.TryParseExact(Session, "N", out id) || string.IsNullOrEmpty(Scene) || Scene.Length > 128)
                throw new InvalidDataException("Invalid scene identity");
            if (Sequence < 0 || (Request != null && !Guid.TryParseExact(Request, "N", out id)))
                throw new InvalidDataException("Invalid sequence/request");
            if (BaseHeight.HasValue && (double.IsNaN(BaseHeight.Value) || double.IsInfinity(BaseHeight.Value) || Math.Abs(BaseHeight.Value) > 100000))
                throw new InvalidDataException("Invalid origin");
            if (Cell != null) Cell.Validate();
            if (Blocks != null)
            {
                if (Blocks.Length > Wire.MaxSnapshotBlocks) throw new InvalidDataException("Snapshot budget exceeded");
                foreach (Cell cell in Blocks) { if (cell == null) throw new InvalidDataException("Null cell"); cell.Validate(); }
            }
            if ((Type == "PlaceBlockRequest" || Type == "BreakBlockRequest" || Type == "BlockDelta") && Cell == null)
                throw new InvalidDataException("Missing cell");
            if ((Type == "PlaceBlockRequest" || Type == "BreakBlockRequest" || Type == "ClearRequest") && Request == null)
                throw new InvalidDataException("Missing request ID");
            if (Type == "PlaceBlockRequest" && !BaseHeight.HasValue) throw new InvalidDataException("Missing origin");
            if (Type == "Snapshot" && (Blocks == null || (Blocks.Length > 0 && !BaseHeight.HasValue)))
                throw new InvalidDataException("Invalid snapshot");
            if (Type == "BlockDelta" && !BaseHeight.HasValue) throw new InvalidDataException("Missing delta origin");
            if (Error != null && Error.Length > 256) throw new InvalidDataException("Error too long");
        }
    }

    public static class Wire
    {
        public const int MaxFrame = 1024 * 1024;
        public const int MaxSnapshotBlocks = 8192; // Transport/memory safety budget, not a rendering performance claim.
        public static void Write(Stream stream, Message message)
        {
            message.Validate();
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Message)).WriteObject(buffer, message);
                if (buffer.Length > MaxFrame) throw new InvalidDataException("Frame too large");
                int length = (int)buffer.Length;
                byte[] prefix = { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
                stream.Write(prefix, 0, 4);
                buffer.Position = 0;
                buffer.CopyTo(stream);
                stream.Flush();
            }
        }
        public static Message Read(Stream stream)
        {
            byte[] prefix = ReadExactly(stream, 4);
            uint length = ((uint)prefix[0] << 24) | ((uint)prefix[1] << 16) | ((uint)prefix[2] << 8) | prefix[3];
            if (length == 0 || length > MaxFrame) throw new InvalidDataException("Invalid frame length");
            using (var buffer = new MemoryStream(ReadExactly(stream, (int)length)))
            {
                var message = (Message)new DataContractJsonSerializer(typeof(Message)).ReadObject(buffer);
                if (message == null) throw new InvalidDataException("Null message");
                message.Validate();
                return message;
            }
        }
        private static byte[] ReadExactly(Stream stream, int length)
        {
            byte[] bytes = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int count = stream.Read(bytes, offset, length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
            return bytes;
        }
    }
}
