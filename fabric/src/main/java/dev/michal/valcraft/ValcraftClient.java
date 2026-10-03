package dev.michal.valcraft;

import com.google.gson.JsonObject;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.network.chat.Component;

public class ValcraftClient implements ClientModInitializer {
	private final Link link = new Link();
	private final RemoteInput input = new RemoteInput();
	private double lastGroundY;
	private long tick;

	@Override
	public void onInitializeClient() {
		link.start();
		// Read Valheim's input before Minecraft processes key bindings for this tick.
		ClientTickEvents.START_CLIENT_TICK.register(this::receive);
		ClientTickEvents.END_CLIENT_TICK.register(this::sendState);
	}

	private void receive(Minecraft mc) {
		// You play through the Valheim window, so Minecraft must keep running unfocused.
		if (mc.options != null) mc.options.pauseOnLostFocus = false;
		JsonObject msg;
		while ((msg = link.poll()) != null) {
			handle(mc, msg);
		}
	}

	private void sendState(Minecraft mc) {
		LocalPlayer player = mc.player;
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
		s.addProperty("fov", mc.options.fov().get());
		s.addProperty("sens", mc.options.sensitivity().get());
		s.addProperty("ground", player.onGround());
		s.addProperty("sneak", player.isShiftKeyDown());
		s.addProperty("sprint", player.isSprinting());
		s.addProperty("swim", player.isSwimming());
		s.addProperty("elytra", player.isFallFlying());
		s.addProperty("slot", player.getInventory().getSelectedSlot());
		s.addProperty("item", player.getInventory().getSelectedItem().getHoverName().getString());
		link.send(s);
	}

	private void handle(Minecraft mc, JsonObject msg) {
		String type = msg.has("t") ? msg.get("t").getAsString() : "";
		if (type.equals("_link")) {
			boolean up = msg.get("state").getAsString().equals("connected");
			say(mc, up ? "Linked to Valheim" : "Valheim link lost");
		} else if (type.equals("input")) {
			input.apply(mc, msg);
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
