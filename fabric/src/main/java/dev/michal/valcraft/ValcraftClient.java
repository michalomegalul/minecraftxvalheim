package dev.michal.valcraft;

import com.google.gson.JsonObject;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.phys.Vec3;

import java.util.Set;

public class ValcraftClient implements ClientModInitializer {
	private static final Link link = new Link();
	private final RemoteInput input = new RemoteInput();
	private static final Terrain terrain = new Terrain(link::send);
	private static final BlockMirror blocks = new BlockMirror(link::send, terrain);

	// Set by a "teleport" from Valheim: hold the player here until the ground under them exists.
	private boolean frozen;
	private double fx, fy, fz;
	private boolean warnedNotSingleplayer;
	private double lastGroundY;
	private long tick;

	public static boolean isLinked() {
		return link.isConnected();
	}

	@Override
	public void onInitializeClient() {
		link.start();
		// Read Valheim's input before Minecraft processes key bindings for this tick.
		ClientTickEvents.START_CLIENT_TICK.register(this::receive);
		ClientTickEvents.END_CLIENT_TICK.register(this::sendState);
		// Valheim's terrain is written on the integrated (singleplayer) server.
		ServerTickEvents.END_SERVER_TICK.register(server -> {
			if (link.isConnected()) terrain.tick(server.overworld());
		});
	}

	private void receive(Minecraft mc) {
		// You play through the Valheim window, so Minecraft must keep running unfocused.
		if (mc.options != null) mc.options.pauseOnLostFocus = false;
		JsonObject msg;
		while ((msg = link.poll()) != null) {
			handle(mc, msg);
		}
		LocalPlayer player = mc.player;
		if (frozen && player != null) {
			player.setPos(fx, fy, fz);
			player.setDeltaMovement(Vec3.ZERO);
			if (terrain.isWritten(Math.floorDiv((int) Math.floor(fx), 16), Math.floorDiv((int) Math.floor(fz), 16))) {
				frozen = false;
				JsonObject ready = new JsonObject();
				ready.addProperty("t", "ready");
				link.send(ready);
				say(mc, "Synced with Valheim");
			}
		}
	}

	private void sendState(Minecraft mc) {
		LocalPlayer player = mc.player;
		if (player == null || !link.isConnected()) return;
		blocks.tick(mc);

		if (player.onGround()) lastGroundY = player.getY();

		JsonObject s = new JsonObject();
		s.addProperty("t", "player");
		s.addProperty("tick", tick++);
		s.addProperty("x", player.getX());
		s.addProperty("y", player.getY());
		s.addProperty("z", player.getZ());
		// Height above the last floor we stood on. Until Valheim's terrain is fed into
		// Minecraft (step 3), Valheim places us at its own ground height plus this.
		s.addProperty("air", player.onGround() ? 0.0 : player.getY() - lastGroundY);
		s.addProperty("yaw", player.getYRot());
		s.addProperty("pitch", player.getXRot());
		s.addProperty("eye", player.getEyeHeight());
		s.addProperty("fov", mc.options.fov().get());
		s.addProperty("sens", mc.options.sensitivity().get());
		s.addProperty("ground", player.onGround());
		s.addProperty("sneak", player.isShiftKeyDown());
		s.addProperty("sprint", player.isSprinting());
		s.addProperty("swim", player.isSwimming());
		s.addProperty("elytra", player.isFallFlying());
		s.addProperty("slot", player.getInventory().getSelectedSlot());
		s.addProperty("item", player.getInventory().getSelectedItem().getHoverName().getString());
		s.addProperty("frozen", frozen);
		s.addProperty("screen", mc.gui.screen() != null);
		link.send(s);
	}

	private void handle(Minecraft mc, JsonObject msg) {
		String type = msg.has("t") ? msg.get("t").getAsString() : "";
		if (type.equals("_link")) {
			boolean up = msg.get("state").getAsString().equals("connected");
			// A new link starts from scratch: Valheim resends terrain and syncs again.
			frozen = false;
			terrain.clear();
			CollisionField.clear();
			blocks.reset();
			say(mc, up ? "Linked to Valheim" : "Valheim link lost");
		} else if (type.equals("input")) {
			if (!frozen) input.apply(mc, msg);
		} else if (type.equals("screen_input")) {
			input.applyScreen(mc, msg);
		} else if (type.equals("chunk")) {
			if (msg.has("boxes")) {
				CollisionField.put(msg.get("cx").getAsInt(), msg.get("cz").getAsInt(), msg.getAsJsonArray("boxes"));
			}
			if (msg.has("top")) terrain.enqueue(msg);
		} else if (type.equals("teleport")) {
			teleport(mc, msg);
		} else if (type.equals("hello")) {
			say(mc, "Valheim says hi: " + msg.get("version").getAsString());
		}
	}

	private void teleport(Minecraft mc, JsonObject msg) {
		var server = mc.getSingleplayerServer();
		if (server == null || mc.player == null) {
			if (!warnedNotSingleplayer) say(mc, "Valcraft needs a singleplayer world (Superflat, preset: The Void)");
			warnedNotSingleplayer = true;
			return;
		}
		fx = msg.get("x").getAsDouble();
		fy = msg.get("y").getAsDouble();
		fz = msg.get("z").getAsDouble();
		float yaw = msg.get("yaw").getAsFloat(), pitch = msg.get("pitch").getAsFloat();
		frozen = true;
		var uuid = mc.player.getUUID();
		server.execute(() -> {
			ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
			if (sp != null) sp.teleportTo(server.overworld(), fx, fy, fz, Set.of(), yaw, pitch, false);
		});
		say(mc, String.format("Syncing with Valheim at %.0f %.0f %.0f...", fx, fy, fz));
	}

	private static void say(Minecraft mc, String text) {
		if (mc.player != null) {
			mc.player.sendSystemMessage(Component.literal("[Valcraft] " + text));
		}
	}
}
