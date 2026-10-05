package dev.bannerlordblocks;

import dev.bannerlordblocks.BridgeProtocol.Cell;
import dev.bannerlordblocks.BridgeProtocol.Message;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/** Standalone deterministic tests. No Minecraft world or server is created. */
public final class BridgeChecks {
    private static int checks;
    private static final String SESSION = "1234567890abcdef1234567890abcdef";
    private static final String SCENE = "bridge_test";
    private static void check(boolean condition, String name) {
        if (!condition) throw new AssertionError(name); checks++;
    }
    private interface Checked { void run() throws Exception; }
    private static void rejected(Checked action, String name) throws Exception {
        try { action.run(); } catch (IOException | IllegalStateException ex) { checks++; return; }
        throw new AssertionError(name);
    }
    private static Message request(String type, Cell cell) {
        Message m = Message.of(type, SESSION, SCENE); m.Cell = cell;
        m.Request = UUID.randomUUID().toString().replace("-", ""); m.BaseHeight = 12.4476; return m;
    }
    private static List<Message> finish(StoneAuthority.Job job, List<Message> output) throws IOException {
        int ticks = 0;
        while (!job.step(1)) if (++ticks > 10000) throw new AssertionError("Job never completed");
        return output;
    }
    public static void main(String[] args) throws Exception {
        Message place = request("PlaceBlockRequest", new Cell(-120, 1029, -2));
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        BridgeProtocol.write(new DataOutputStream(bytes), place);
        Message copy = BridgeProtocol.read(new DataInputStream(new ByteArrayInputStream(bytes.toByteArray())));
        check(copy.Cell.equals(place.Cell) && copy.BaseHeight.equals(place.BaseHeight), "JSON frame roundtrip");
        rejected(() -> BridgeProtocol.read(new DataInputStream(new ByteArrayInputStream(new byte[4]))), "Zero frame");
        rejected(() -> BridgeProtocol.read(new DataInputStream(new ByteArrayInputStream(new byte[] {127, -1, -1, -1}))), "Oversized frame");
        rejected(() -> BridgeProtocol.read(new DataInputStream(new ByteArrayInputStream(new byte[] {0, 0, 0, 5, 1}))), "Truncated frame");
        Message invalid = request("PlaceBlockRequest", new Cell(Integer.MIN_VALUE, 0, 0));
        rejected(invalid::validate, "Coordinate overflow");
        invalid = request("PlaceBlockRequest", new Cell(0, 0, 0)); invalid.BaseHeight = Double.NaN;
        rejected(invalid::validate, "NaN origin");
        invalid = request("PlaceBlockRequest", new Cell(0, 0, 0)); invalid.Version = 2;
        rejected(invalid::validate, "Version mismatch");
        for (String fragment : List.of("\"Version\":1.5", "\"Sequence\":0.5", "\"Cell\":{\"X\":0.5,\"Y\":0,\"Z\":0}", "\"Version\":\"1\"")) {
            String raw = "{\"Type\":\"Hello\",\"Session\":\"" + SESSION + "\",\"Scene\":\"" + SCENE + "\"," +
                (fragment.startsWith("\"Version\"") ? "" : "\"Version\":1,") + fragment + "}";
            ByteArrayOutputStream bad = new ByteArrayOutputStream();
            byte[] encoded = raw.getBytes(java.nio.charset.StandardCharsets.UTF_8);
            DataOutputStream stream = new DataOutputStream(bad); stream.writeInt(encoded.length); stream.write(encoded);
            rejected(() -> BridgeProtocol.read(new DataInputStream(new ByteArrayInputStream(bad.toByteArray()))), "Strict numeric JSON " + fragment);
        }

        // Test-double backend intentionally kept separate from the production ServerLevel adapter.
        Map<StoneAuthority.Position, StoneAuthority.State> state = new HashMap<>();
        int[] writes = {0};
        StoneAuthority authority = new StoneAuthority(new StoneAuthority.Blocks() {
            public StoneAuthority.State read(StoneAuthority.Position p) { return state.getOrDefault(p, StoneAuthority.State.AIR); }
            public boolean write(StoneAuthority.Position p, StoneAuthority.State s) { state.put(p, s); writes[0]++; return true; }
        });
        List<Message> output = new ArrayList<>();
        finish(authority.begin(Message.of("SceneBegin", SESSION, SCENE), output::addAll), output);
        check(output.getFirst().Blocks.length == 0, "Initial snapshot"); output.clear();
        finish(authority.request(place, output::addAll), output);
        check(writes[0] == 1 && output.getFirst().Type.equals("BlockDelta") && output.getLast().Error == null, "Placement writes backend before delta");
        StoneAuthority.Position pos = authority.position(place.Cell);
        check(pos.equals(new StoneAuthority.Position(0, 80, 0)), "First cell establishes bounded MC origin");
        check(authority.position(new Cell(-119, 1030, -1)).equals(new StoneAuthority.Position(1, 81, 1)), "Host XY/Z to MC XZ/Y mapping");
        output.clear(); finish(authority.request(place, output::addAll), output);
        check(writes[0] == 1 && output.size() == 1 && output.getFirst().Type.equals("Result"), "Request deduplication without stale delta replay");
        state.put(pos, StoneAuthority.State.AIR); // External world edit must drive a delta without a host request.
        List<Message> changes = authority.poll(32);
        check(changes.size() == 1 && !changes.getFirst().Present, "External backend removal observed");
        state.put(pos, StoneAuthority.State.STONE);
        check(authority.poll(32).getFirst().Present, "External backend replacement observed");
        output.clear(); finish(authority.snapshot(output::addAll), output);
        check(output.getFirst().Blocks.length == 1, "Snapshot reads backend");
        output.clear(); finish(authority.begin(Message.of("SceneBegin", SESSION, SCENE), output::addAll), output);
        check(output.getFirst().Blocks.length == 1 && state.get(pos) == StoneAuthority.State.STONE, "Same-session reconnect preserves backend world");
        Message breakBlock = request("BreakBlockRequest", place.Cell);
        output.clear(); finish(authority.request(breakBlock, output::addAll), output);
        check(state.get(pos) == StoneAuthority.State.AIR && !output.getFirst().Present, "Break mutates backend");
        output.clear(); finish(authority.request(request("BreakBlockRequest", new Cell(-119, 1029, -2)), output::addAll), output);
        check(output.getLast().Error.equals("not owned"), "Unknown cells cannot be destroyed");
        output.clear(); finish(authority.request(request("PlaceBlockRequest", new Cell(-87, 1029, -2)), output::addAll), output);
        check(output.getLast().Error.equals("Outside M3 controlled region"), "Region limit");
        Message stale = request("ClearRequest", null); stale.Session = "00000000000000000000000000000000";
        rejected(() -> authority.request(stale, _ -> { }), "Stale session");
        Throwable[] wrongThread = {null};
        Thread worker = new Thread(() -> { try { authority.poll(1); } catch (Throwable ex) { wrongThread[0] = ex; } });
        worker.start(); worker.join(); check(wrongThread[0] instanceof IllegalStateException, "World access thread confinement");

        output.clear(); finish(authority.request(request("PlaceBlockRequest", place.Cell), output::addAll), output);
        output.clear(); finish(authority.request(request("PlaceBlockRequest", new Cell(-119, 1029, -2)), output::addAll), output);
        output.clear(); StoneAuthority.Job clear = authority.request(request("ClearRequest", null), output::addAll);
        check(!clear.step(1) && output.isEmpty(), "Clear yields between bounded batches");
        finish(clear, output); check(output.getFirst().Blocks.length == 0 && output.getFirst().BaseHeight != null, "Clear snapshot preserves origin");
        output.clear(); finish(authority.request(request("PlaceBlockRequest", place.Cell), output::addAll), output);
        state.put(pos, StoneAuthority.State.OTHER);
        rejected(() -> authority.poll(32), "Unsupported state fails closed");
        rejected(() -> finish(authority.shutdown(), new ArrayList<>()), "Foreign state not destroyed on shutdown");
        check(state.get(pos) == StoneAuthority.State.OTHER, "Foreign block retained");
        state.put(pos, StoneAuthority.State.STONE);
        output.clear();
        finish(authority.begin(Message.of("SceneBegin", "00000000000000000000000000000000", SCENE), output::addAll), output);
        check(state.get(pos) == StoneAuthority.State.AIR && output.getFirst().Sequence == 0 && output.getFirst().Blocks.length == 0,
            "New scene cleans old owned world and resets snapshot");
        output.clear(); finish(authority.begin(Message.of("SceneBegin", SESSION, SCENE), output::addAll), output);
        output.clear(); finish(authority.request(request("PlaceBlockRequest", place.Cell), output::addAll), output);
        finish(authority.shutdown(), new ArrayList<>());
        check(state.get(pos) == StoneAuthority.State.AIR, "Owned cleanup on shutdown");

        // Shared wire fixtures are generated by the C# serializer, not Gson.
        if (args.length != 1) throw new AssertionError("Fixture directory required");
        Path fixtures = Path.of(args[0]);
        Path markerDirectory = Files.createTempDirectory(fixtures, "marker-check-");
        Path marker = markerDirectory.resolve("bannerlord-bridge-test-world.txt");
        try {
            rejected(() -> MinecraftBridgeServer.requireTestWorld(markerDirectory), "World marker required");
            Files.writeString(marker, "not approved");
            rejected(() -> MinecraftBridgeServer.requireTestWorld(markerDirectory), "Wrong marker rejected");
            Files.writeString(marker, "M3-STONE-TEST\n");
            MinecraftBridgeServer.requireTestWorld(markerDirectory); check(true, "Explicit test-world marker accepted");
        } finally { Files.deleteIfExists(marker); Files.delete(markerDirectory); }
        for (String name : List.of("csharp-place.frame", "csharp-snapshot.frame")) {
            try (DataInputStream input = new DataInputStream(Files.newInputStream(fixtures.resolve(name)))) {
                Message m = BridgeProtocol.read(input);
                check(m.Session.equals(SESSION) && m.Scene.equals(SCENE), "C# -> Java " + name);
            }
        }
        Message delta = Message.of("BlockDelta", SESSION, SCENE); delta.Cell = place.Cell;
        delta.BaseHeight = 12.4476; delta.Present = true; delta.Sequence = 1;
        try (DataOutputStream stream = new DataOutputStream(Files.newOutputStream(fixtures.resolve("java-delta.frame")))) {
            BridgeProtocol.write(stream, delta);
        }
        System.out.println("PASS: " + checks + " Java framing/authority test-double checks (no Minecraft world started).");
    }
}
