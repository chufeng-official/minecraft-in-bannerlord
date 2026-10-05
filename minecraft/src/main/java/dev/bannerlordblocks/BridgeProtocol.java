package dev.bannerlordblocks;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.util.Set;

/** Protocol v1 mirrors the capitalized DataContract members of the C# client. */
public final class BridgeProtocol {
    public static final int MAX_FRAME = 1024 * 1024;
    public static final int MAX_CELLS = 8192;
    private static final Gson JSON = new GsonBuilder().serializeNulls().create();
    private static final Set<String> TYPES = Set.of("Hello", "SceneBegin", "SceneEnd", "SnapshotRequest",
        "Snapshot", "PlaceBlockRequest", "BreakBlockRequest", "ClearRequest", "BlockDelta", "Result", "Heartbeat");

    public record Cell(int X, int Y, int Z) {
        void validate() throws IOException {
            if (Math.abs((long) X) > 100000 || Math.abs((long) Y) > 100000 || Math.abs((long) Z) > 100000)
                throw new IOException("Cell out of range");
        }
    }

    public static final class Message {
        public int Version = 1;
        public String Type, Session, Scene, Request;
        public long Sequence;
        public Cell Cell;
        public Cell[] Blocks;
        public Double BaseHeight;
        public boolean Present;
        public String Error;

        public static Message of(String type, String session, String scene) {
            Message m = new Message(); m.Type = type; m.Session = session; m.Scene = scene; return m;
        }
        public void validate() throws IOException {
            if (Version != 1 || Type == null || !TYPES.contains(Type)) throw new IOException("Unsupported version/type");
            if (Session == null || !Session.matches("[0-9a-fA-F]{32}") || Scene == null || Scene.isEmpty() || Scene.length() > 128)
                throw new IOException("Invalid scene identity");
            if (Sequence < 0 || Request != null && !Request.matches("[0-9a-fA-F]{32}")) throw new IOException("Invalid request/sequence");
            if (BaseHeight != null && (!Double.isFinite(BaseHeight) || Math.abs(BaseHeight) > 100000)) throw new IOException("Invalid origin");
            if (Cell != null) Cell.validate();
            if (Blocks != null) {
                if (Blocks.length > MAX_CELLS) throw new IOException("Snapshot budget exceeded");
                for (Cell cell : Blocks) { if (cell == null) throw new IOException("Null cell"); cell.validate(); }
            }
            if (Set.of("PlaceBlockRequest", "BreakBlockRequest", "BlockDelta").contains(Type) && Cell == null)
                throw new IOException("Missing cell");
            if (Set.of("PlaceBlockRequest", "BreakBlockRequest", "ClearRequest").contains(Type) && Request == null)
                throw new IOException("Missing request ID");
            if ((Type.equals("PlaceBlockRequest") || Type.equals("BlockDelta")) && BaseHeight == null)
                throw new IOException("Missing origin");
            if (Type.equals("Snapshot") && (Blocks == null || Blocks.length > 0 && BaseHeight == null))
                throw new IOException("Invalid snapshot");
            if (Error != null && Error.length() > 256) throw new IOException("Error too long");
        }
    }

    public static Message read(DataInputStream stream) throws IOException {
        int length = stream.readInt();
        if (length <= 0 || length > MAX_FRAME) throw new IOException("Invalid frame length");
        byte[] bytes = new byte[length]; stream.readFully(bytes);
        try {
            String text = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();
            JsonObject raw = JsonParser.parseString(text).getAsJsonObject();
            if (!raw.has("Version") || raw.get("Version").isJsonNull()) throw new IOException("Missing version");
            number(raw, "Version").intValueExact();
            if (raw.has("Sequence")) number(raw, "Sequence").longValueExact();
            if (raw.has("BaseHeight") && !raw.get("BaseHeight").isJsonNull()) number(raw, "BaseHeight");
            for (String name : Set.of("Type", "Session", "Scene", "Request", "Error"))
                if (raw.has(name) && !raw.get(name).isJsonNull() &&
                    (!raw.get(name).isJsonPrimitive() || !raw.get(name).getAsJsonPrimitive().isString()))
                    throw new IOException("Invalid string field");
            if (raw.has("Present") && (!raw.get("Present").isJsonPrimitive() || !raw.get("Present").getAsJsonPrimitive().isBoolean()))
                throw new IOException("Invalid presence field");
            if (raw.has("Cell") && !raw.get("Cell").isJsonNull()) {
                cell(raw.getAsJsonObject("Cell"));
            }
            if (raw.has("Blocks") && !raw.get("Blocks").isJsonNull()) {
                var cells = raw.getAsJsonArray("Blocks");
                if (cells.size() > MAX_CELLS) throw new IOException("Snapshot budget exceeded");
                for (var value : cells) cell(value.getAsJsonObject());
            }
            Message message = JSON.fromJson(raw, Message.class); message.validate(); return message;
        } catch (RuntimeException ex) { throw new IOException("Invalid JSON message", ex); }
    }

    private static java.math.BigDecimal number(JsonObject object, String name) throws IOException {
        if (!object.has(name) || !object.get(name).isJsonPrimitive() || !object.get(name).getAsJsonPrimitive().isNumber())
            throw new IOException("Invalid numeric field");
        return object.get(name).getAsBigDecimal();
    }
    private static void cell(JsonObject cell) throws IOException {
        for (String axis : Set.of("X", "Y", "Z")) number(cell, axis).intValueExact();
    }

    public static void write(DataOutputStream stream, Message message) throws IOException {
        message.validate();
        byte[] bytes = JSON.toJson(message).getBytes(StandardCharsets.UTF_8);
        if (bytes.length > MAX_FRAME) throw new IOException("Frame too large");
        stream.writeInt(bytes.length); stream.write(bytes); stream.flush();
    }

    private BridgeProtocol() { }
}
