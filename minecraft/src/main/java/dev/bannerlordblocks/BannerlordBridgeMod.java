package dev.bannerlordblocks;

import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Runtime authority is explicitly opt-in and restricted to the Mod's bridge dimension. */
public final class BannerlordBridgeMod implements ModInitializer {
    static final Logger LOGGER = LoggerFactory.getLogger("bannerlord_bridge");
    private MinecraftBridgeServer service;

    @Override
    public void onInitialize() {
        LOGGER.info("M3 initialized; Minecraft 26.3; enable only in a dedicated test instance with -Dbannerlord.bridge.enabled=true");
        ServerLifecycleEvents.SERVER_STARTED.register(server -> {
            if (!Boolean.getBoolean("bannerlord.bridge.enabled")) return;
            try { service = new MinecraftBridgeServer(server, Integer.getInteger("bannerlord.bridge.port", 25575)); }
            catch (Exception ex) { LOGGER.error("M3 bridge not started: {}", ex.toString()); }
        });
        ServerTickEvents.END_SERVER_TICK.register(server -> {
            if (service != null && service.owns(server)) service.tick();
        });
        ServerLifecycleEvents.SERVER_STOPPING.register(server -> {
            if (service != null && service.owns(server)) { service.close(); service = null; }
        });
    }
}
