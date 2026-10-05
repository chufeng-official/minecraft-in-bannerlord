package dev.bannerlordblocks;

import dev.bannerlordblocks.BridgeProtocol.Message;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.Path;
import java.util.List;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.storage.LevelResource;

/** Network threads only frame messages; tick() owns every Minecraft operation. */
public final class MinecraftBridgeServer implements AutoCloseable {
    public static final ResourceKey<Level> DIMENSION = ResourceKey.create(Registries.DIMENSION,
        Identifier.fromNamespaceAndPath("bannerlord_bridge", "bridge"));
    private record Work(Peer peer, Message message) { }
    private final MinecraftServer server;
    private final StoneAuthority authority;
    private final ServerSocket listener = new ServerSocket();
    private final ArrayBlockingQueue<Work> requests = new ArrayBlockingQueue<>(64);
    private volatile boolean stopped;
    private volatile Peer connected;
    private Peer active;
    private Work running;
    private StoneAuthority.Job job;

    public MinecraftBridgeServer(MinecraftServer server, int port) throws IOException {
        this.server = server;
        try { requireTestWorld(server.getWorldPath(LevelResource.ROOT)); }
        catch (IOException ex) { listener.close(); throw ex; }
        ServerLevel level = server.getLevel(DIMENSION);
        if (level == null) { listener.close(); throw new IOException("Bridge dimension absent; use a dedicated new test world with this Mod enabled"); }
        if (port < 1024 || port > 65535) { listener.close(); throw new IOException("Invalid bridge port"); }
        authority = new StoneAuthority(new StoneAuthority.Blocks() {
            private BlockPos pos(StoneAuthority.Position p) { return new BlockPos(p.x(), p.y(), p.z()); }
            @Override public StoneAuthority.State read(StoneAuthority.Position p) {
                var state = level.getBlockState(pos(p));
                return state.isAir() ? StoneAuthority.State.AIR : state.is(Blocks.STONE) ? StoneAuthority.State.STONE : StoneAuthority.State.OTHER;
            }
            @Override public boolean write(StoneAuthority.Position p, StoneAuthority.State state) {
                return level.setBlock(pos(p), (state == StoneAuthority.State.STONE ? Blocks.STONE : Blocks.AIR).defaultBlockState(),
                    Block.UPDATE_ALL);
            }
        });
        try { listener.bind(new InetSocketAddress(InetAddress.getByName("127.0.0.1"), port), 1); }
        catch (IOException ex) { listener.close(); throw ex; }
        Thread accept = new Thread(this::accept, "Minecraft Bannerlord bridge accept");
        accept.setDaemon(true); accept.start();
        BannerlordBridgeMod.LOGGER.info("M3 real-world authority listening on 127.0.0.1:{}; dimension={}", port, DIMENSION);
    }
    public boolean owns(MinecraftServer candidate) { return candidate == server; }

    static void requireTestWorld(Path world) throws IOException {
        Path marker = world.resolve("bannerlord-bridge-test-world.txt");
        if (!Files.isRegularFile(marker, LinkOption.NOFOLLOW_LINKS) || Files.size(marker) > 64 ||
            !Files.readString(marker).strip().equals("M3-STONE-TEST"))
            throw new IOException("Test-world authorization marker absent/invalid; bridge remains disabled");
    }

    private void accept() {
        while (!stopped) {
            try {
                Socket socket = listener.accept();
                Peer peer = new Peer(socket); connected = peer;
                peer.run(); // One connection at a time; replaces neither unknown processes nor other hosts.
            } catch (IOException | RuntimeException ex) {
                if (!stopped) BannerlordBridgeMod.LOGGER.warn("M3 connection ended: {}", ex.getClass().getSimpleName());
            } finally {
                Peer peer = connected; if (peer != null) peer.close(); connected = null;
            }
        }
    }

    public void tick() {
        if (stopped) return;
        long start = System.nanoTime();
        boolean stepped = false;
        try {
            // Large cleanup and snapshots are split into 32 world operations per tick.
            for (int commands = 0; commands < 16; commands++) {
                if (job != null) {
                    if (!running.peer.alive && !running.message.Type.equals("SceneEnd")) { job = null; running = null; }
                    else {
                        stepped = true;
                        if (job.step(32)) {
                            if (running.message.Type.equals("SceneEnd")) { running.peer.close(); active = null; }
                            job = null; running = null;
                        }
                        break;
                    }
                }
                Work work = requests.poll();
                if (work == null) break;
                if (!work.peer.alive && !(work.message.Type.equals("SceneEnd") && active == work.peer)) continue;
                running = work;
                if (work.message.Type.equals("SceneBegin")) {
                    active = work.peer;
                    job = authority.begin(work.message, work.peer::publish);
                } else {
                    if (active != work.peer) throw new IOException("Peer has no active scene");
                    job = authority.request(work.message, work.peer::publish);
                }
                if (System.nanoTime() - start >= 4_000_000) break;
            }
            if (!stepped && job == null && active != null && active.alive && System.nanoTime() - start < 4_000_000) {
                List<Message> changes = authority.poll(32);
                active.publish(changes);
            }
        } catch (IOException | RuntimeException ex) {
            BannerlordBridgeMod.LOGGER.error("M3 world request disabled for peer: {}", ex.toString());
            if (running != null) running.peer.close();
            if (active != null) active.close();
            running = null; job = null; active = null;
        }
    }

    @Override public void close() {
        stopped = true;
        try { listener.close(); } catch (IOException ignored) { }
        Peer peer = connected; if (peer != null) peer.close(); requests.clear();
        // Called by SERVER_STOPPING while world writes are still on the owning server thread.
        try {
            StoneAuthority.Job cleanup = authority.shutdown();
            while (!cleanup.step(32)) { }
        } catch (IOException | RuntimeException ex) {
            BannerlordBridgeMod.LOGGER.error("M3 shutdown cleanup incomplete; do not reuse this test world's bridge region: {}", ex.toString());
        }
    }

    private final class Peer implements AutoCloseable {
        private final Socket socket;
        private final ArrayBlockingQueue<Message> outgoing = new ArrayBlockingQueue<>(128);
        private volatile boolean alive = true;
        Peer(Socket socket) throws IOException {
            this.socket = socket; socket.setTcpNoDelay(true); socket.setSoTimeout(10000);
        }
        void publish(List<Message> messages) {
            for (Message message : messages) {
                if (!alive) return;
                if (!outgoing.offer(message)) { close(); return; }
            }
        }
        void run() throws IOException {
            try {
                DataInputStream input = new DataInputStream(socket.getInputStream());
                DataOutputStream output = new DataOutputStream(socket.getOutputStream());
                Message hello = BridgeProtocol.read(input);
                if (!hello.Type.equals("Hello")) throw new IOException("Hello required");
                BridgeProtocol.write(output, Message.of("Hello", hello.Session, hello.Scene));
                Message begin = BridgeProtocol.read(input);
                if (!begin.Type.equals("SceneBegin") || !sameScene(begin, hello)) throw new IOException("SceneBegin required");
                Thread writer = new Thread(() -> write(output), "Minecraft Bannerlord bridge writer");
                writer.setDaemon(true); writer.start();
                // A maximum-size reconnect snapshot may require ~13 seconds of world ticks.
                socket.setSoTimeout(30000);
                enqueue(begin);
                while (alive && !stopped) {
                    Message request = BridgeProtocol.read(input);
                    if (!sameScene(request, hello)) throw new IOException("Stale scene");
                    if (request.Type.equals("Heartbeat")) publish(List.of(Message.of("Heartbeat", hello.Session, hello.Scene)));
                    else enqueue(request);
                }
            } finally { close(); }
        }
        private boolean sameScene(Message a, Message b) { return a.Session.equals(b.Session) && a.Scene.equals(b.Scene); }
        private void enqueue(Message request) throws IOException {
            if (!requests.offer(new Work(this, request))) throw new IOException("Server queue budget exceeded");
        }
        private void write(DataOutputStream output) {
            try {
                while (alive && !stopped) {
                    Message message = outgoing.poll(200, TimeUnit.MILLISECONDS);
                    if (message != null) {
                        BridgeProtocol.write(output, message);
                        if (message.Type.equals("Snapshot")) socket.setSoTimeout(10000);
                    }
                }
            } catch (IOException | InterruptedException ex) { close(); }
        }
        @Override public void close() {
            alive = false; try { socket.close(); } catch (IOException ignored) { }
        }
    }
}
