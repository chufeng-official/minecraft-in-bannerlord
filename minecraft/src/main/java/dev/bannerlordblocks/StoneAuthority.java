package dev.bannerlordblocks;

import dev.bannerlordblocks.BridgeProtocol.Cell;
import dev.bannerlordblocks.BridgeProtocol.Message;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Consumer;

/** All methods and block access run on the owning Minecraft server thread. */
public final class StoneAuthority {
    public enum State { AIR, STONE, OTHER }
    public record Position(int x, int y, int z) { }
    public interface Blocks {
        State read(Position position);
        boolean write(Position position, State state);
    }
    public interface Job { boolean step(int budget) throws IOException; }
    private final Blocks blocks;
    private final Thread owner = Thread.currentThread();
    // This is an ownership/watch index, NOT a simulated world. Each operation reads the real backend.
    private final LinkedHashMap<Cell, Boolean> watched = new LinkedHashMap<>();
    private final LinkedHashMap<String, List<Message>> results = new LinkedHashMap<>();
    private Iterator<Cell> scan;
    private String session, scene;
    private Cell anchor;
    private Double baseHeight;
    private long sequence;

    public StoneAuthority(Blocks blocks) { this.blocks = blocks; }
    private void checkThread() {
        if (Thread.currentThread() != owner) throw new IllegalStateException("World accessed off server thread");
    }
    public Position position(Cell cell) throws IOException {
        Cell origin = anchor == null ? cell : anchor;
        long x = (long) cell.X() - origin.X(), z = (long) cell.Y() - origin.Y(), y = (long) cell.Z() - origin.Z();
        if (x < -32 || x > 31 || z < -32 || z > 31 || y < -32 || y > 63)
            throw new IOException("Outside M3 controlled region");
        return new Position((int) x, 80 + (int) y, (int) z);
    }
    private boolean present(Cell cell) throws IOException {
        State state = blocks.read(position(cell));
        if (state == State.OTHER) throw new IOException("Unsupported block in watched bridge cell");
        return state == State.STONE;
    }
    private boolean setState(Position position, State state) {
        blocks.write(position, state);
        // The state read back from Minecraft is authoritative, not the setter's change flag.
        return blocks.read(position) == state;
    }
    private Message message(String type) { return Message.of(type, session, scene); }
    private Message delta(Cell cell, boolean present) {
        Message m = message("BlockDelta"); m.Cell = cell; m.Present = present;
        m.BaseHeight = baseHeight; m.Sequence = ++sequence; return m;
    }
    private void observe(Cell cell, boolean present) {
        if (watched.get(cell) != present) { watched.put(cell, present); sequence++; }
    }

    public Job begin(Message begin, Consumer<List<Message>> output) throws IOException {
        checkThread(); begin.validate();
        if (!begin.Type.equals("SceneBegin")) throw new IOException("SceneBegin required");
        if (begin.Session.equals(session) && begin.Scene.equals(scene)) return snapshot(output);
        Iterator<Cell> cleanup = new ArrayList<>(watched.keySet()).iterator();
        return budget -> {
            checkThread();
            for (int i = 0; i < budget && cleanup.hasNext(); i++) removeOwned(cleanup.next());
            if (cleanup.hasNext()) return false;
            watched.clear(); results.clear(); scan = null;
            anchor = null; baseHeight = null; sequence = 0; session = begin.Session; scene = begin.Scene;
            output.accept(List.of(emptySnapshot())); return true;
        };
    }
    private Message emptySnapshot() {
        Message m = message("Snapshot"); m.Blocks = new Cell[0]; m.BaseHeight = baseHeight; m.Sequence = sequence; return m;
    }
    public Job snapshot(Consumer<List<Message>> output) {
        checkThread();
        Iterator<Cell> cells = new ArrayList<>(watched.keySet()).iterator();
        List<Cell> stones = new ArrayList<>();
        return budget -> {
            checkThread();
            for (int i = 0; i < budget && cells.hasNext(); i++) {
                Cell cell = cells.next(); boolean present = present(cell); observe(cell, present);
                if (present) stones.add(cell);
            }
            if (cells.hasNext()) return false;
            Message m = emptySnapshot(); m.Blocks = stones.toArray(Cell[]::new); output.accept(List.of(m)); return true;
        };
    }

    public Job request(Message request, Consumer<List<Message>> output) throws IOException {
        checkThread(); request.validate();
        if (!request.Session.equals(session) || !request.Scene.equals(scene)) throw new IOException("Stale scene");
        if (request.Type.equals("SnapshotRequest")) return snapshot(output);
        if (request.Type.equals("SceneEnd")) return clear(() -> {
            watched.clear(); results.clear(); anchor = null; baseHeight = null; session = null; scene = null; scan = null;
            output.accept(List.of());
        });
        if (!List.of("PlaceBlockRequest", "BreakBlockRequest", "ClearRequest").contains(request.Type))
            throw new IOException("Unexpected request");
        if (results.containsKey(request.Request)) {
            // Re-emit only the cached result; old clear snapshots must never roll a live replica backwards.
            List<Message> cached = results.get(request.Request);
            output.accept(List.of(cached.getLast())); return _ -> true;
        }
        if (request.Type.equals("ClearRequest")) return clear(() -> {
            sequence++; scan = null;
            complete(request, List.of(emptySnapshot()), null, output);
        });
        String error = null; List<Message> changes = new ArrayList<>();
        Cell cell = request.Cell;
        Position position;
        try { position = position(cell); }
        catch (IOException ex) { complete(request, changes, ex.getMessage(), output); return _ -> true; }
        if (request.Type.equals("PlaceBlockRequest")) {
            if (baseHeight != null && Math.abs(baseHeight - request.BaseHeight) > 0.001) error = "origin mismatch";
            else if (watched.size() >= BridgeProtocol.MAX_CELLS && !watched.containsKey(cell)) error = "M3 watch budget exceeded";
            else if (blocks.read(position) != State.AIR) error = "occupied in Minecraft";
            else if (!setState(position, State.STONE)) error = "Minecraft placement failed";
            else {
                anchor = anchor == null ? cell : anchor; baseHeight = baseHeight == null ? request.BaseHeight : baseHeight;
                watched.put(cell, true); scan = null;
                Message change = delta(cell, true); change.Request = request.Request; changes.add(change);
                BannerlordBridgeMod.LOGGER.info("MC stone placed: host={} mc={} session={}", cell, position, session);
            }
        } else if (!watched.containsKey(cell)) error = "not owned";
        else if (!present(cell)) error = "not found";
        else if (!setState(position, State.AIR)) error = "Minecraft removal failed";
        else {
            watched.put(cell, false); Message change = delta(cell, false); change.Request = request.Request; changes.add(change);
            BannerlordBridgeMod.LOGGER.info("MC stone removed: host={} mc={} session={}", cell, position, session);
        }
        complete(request, changes, error, output); return _ -> true;
    }

    private void complete(Message request, List<Message> changes, String error, Consumer<List<Message>> output) {
        Message result = message("Result"); result.Request = request.Request; result.Error = error; result.Sequence = sequence;
        List<Message> response = new ArrayList<>(changes); response.add(result);
        results.put(request.Request, List.copyOf(response));
        if (results.size() > 1024) results.remove(results.keySet().iterator().next());
        output.accept(response);
    }
    private void removeOwned(Cell cell) throws IOException {
        Position position = position(cell);
        State state = blocks.read(position);
        // Never destroy an unrecognized block another mod/player placed in this cell.
        if (state == State.OTHER) throw new IOException("Cleanup blocked by unsupported world state");
        if (state == State.STONE && !setState(position, State.AIR))
            throw new IOException("Minecraft cleanup failed");
        watched.put(cell, false);
    }
    private Job clear(Runnable done) {
        Iterator<Cell> cleanup = new ArrayList<>(watched.keySet()).iterator();
        return budget -> {
            checkThread();
            for (int i = 0; i < budget && cleanup.hasNext(); i++) removeOwned(cleanup.next());
            if (cleanup.hasNext()) return false;
            done.run(); return true;
        };
    }
    public List<Message> poll(int budget) throws IOException {
        checkThread(); List<Message> changes = new ArrayList<>();
        if (session == null || baseHeight == null || watched.isEmpty()) return changes;
        if (scan == null || !scan.hasNext()) scan = new ArrayList<>(watched.keySet()).iterator();
        for (int i = 0; i < budget && scan.hasNext(); i++) {
            Cell cell = scan.next(); boolean present = present(cell);
            if (watched.get(cell) != present) { watched.put(cell, present); changes.add(delta(cell, present)); }
        }
        return changes;
    }

    public Job shutdown() {
        checkThread();
        return clear(() -> { watched.clear(); results.clear(); scan = null; session = null; scene = null; });
    }
}
