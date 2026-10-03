package dev.michal.valcraft;

import com.google.gson.JsonObject;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.network.chat.Component;

public class ValcraftClient implements ClientModInitializer {
	private final Link link = new Link();
	private double lastGroundY;
	private long tick;

	@Override
	public void onInitializeClient() {
		link.start();
		ClientTickEvents.END_CLIENT_TICK.register(this::onTick);
	}

	private void onTick(Minecraft mc) {
		LocalPlayer player = mc.player;

		JsonObject msg;
		while ((msg = link.poll()) != null) {
			handle(mc, msg);
		}
		if (player == null || !link.isConnected()) return;

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
		s.addProperty("ground", player.onGround());
		s.addProperty("sneak", player.isShiftKeyDown());
		s.addProperty("sprint", player.isSprinting());
		s.addProperty("swim", player.isSwimming());
		s.addProperty("elytra", player.isFallFlying());
		link.send(s);
	}

	private void handle(Minecraft mc, JsonObject msg) {
		String type = msg.has("t") ? msg.get("t").getAsString() : "";
		if (type.equals("_link")) {
			boolean up = msg.get("state").getAsString().equals("connected");
			say(mc, up ? "Linked to Valheim" : "Valheim link lost");
		} else if (type.equals("hello")) {
			say(mc, "Valheim says hi: " + msg.get("version").getAsString());
		}
	}

	private static void say(Minecraft mc, String text) {
		if (mc.player != null) {
			mc.player.sendSystemMessage(Component.literal("[Valcraft] " + text));
		}
	}
}
