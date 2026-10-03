package dev.michal.valcraft;

import com.google.gson.JsonObject;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Options;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.client.input.MouseButtonInfo;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.MouseHandler;
import dev.michal.valcraft.mixin.MouseHandlerAccessor;

import java.util.HashMap;
import java.util.Map;

/** Applies keyboard/mouse input forwarded from the Valheim window. */
final class RemoteInput {
	private final Map<String, Boolean> down = new HashMap<>();
	private final boolean[] screenButtons = new boolean[3];
	private double lastX = -1, lastY = -1;

	void apply(Minecraft mc, JsonObject msg) {
		LocalPlayer player = mc.player;
		if (player == null) return;
		Options o = mc.options;

		if (msg.has("keys")) {
			JsonObject k = msg.getAsJsonObject("keys");
			press(o.keyUp, k, "forward");
			press(o.keyDown, k, "back");
			press(o.keyLeft, k, "left");
			press(o.keyRight, k, "right");
			press(o.keyJump, k, "jump");
			press(o.keyShift, k, "sneak");
			press(o.keySprint, k, "sprint");
			press(o.keyDrop, k, "drop");
			press(o.keyAttack, k, "attack");
			press(o.keyUse, k, "use");
			press(o.keyInventory, k, "inventory");
			for (int i = 0; i < 9; i++) {
				press(o.keyHotbarSlots[i], k, "hotbar" + (i + 1));
			}
		}
		// Valheim owns the look direction (so its camera has no lag); we just follow it.
		if (msg.has("yaw")) {
			player.setYRot(msg.get("yaw").getAsFloat());
			player.setXRot(msg.get("pitch").getAsFloat());
		}
		if (msg.has("sky")) ValcraftClient.valheimSky = msg.get("sky").getAsInt();
		if (msg.has("scroll")) {
			int slot = player.getInventory().getSelectedSlot() - msg.get("scroll").getAsInt();
			player.getInventory().setSelectedSlot(Math.floorMod(slot, 9));
		}
	}

	/**
	 * Mouse over an open Minecraft screen (inventory, crafting, chests), in Minecraft window
	 * coordinates scaled 0..1 from the top-left, as Valheim draws it.
	 */
	void applyScreen(Minecraft mc, JsonObject msg) {
		var screen = mc.gui.screen();
		if (screen == null) return;
		if (msg.has("close") && msg.get("close").getAsBoolean()) {
			screen.onClose();
			return;
		}
		var window = mc.getWindow();
		long handle = window.handle();
		MouseHandlerAccessor mouse = (MouseHandlerAccessor) mc.mouseHandler;
		double x = msg.get("x").getAsDouble() * window.getScreenWidth();
		double y = msg.get("y").getAsDouble() * window.getScreenHeight();
		int mods = msg.has("shift") && msg.get("shift").getAsBoolean() ? 1 : 0; // GLFW_MOD_SHIFT
		if (x != lastX || y != lastY) {
			mouse.valcraft$onMove(handle, x, y);
			// Minecraft only passes movement to screens while its window is active, and it's
			// minimized: call the screen ourselves, so hovering (tooltips) and dragging work.
			double sx = mc.mouseHandler.getScaledXPos(window), sy = mc.mouseHandler.getScaledYPos(window);
			screen.mouseMoved(sx, sy);
			if (lastX >= 0) {
				double dx = MouseHandler.getScaledXPos(window, x - lastX), dy = MouseHandler.getScaledYPos(window, y - lastY);
				for (int b = 0; b < 3; b++) {
					if (screenButtons[b]) screen.mouseDragged(new MouseButtonEvent(sx, sy, new MouseButtonInfo(b, mods)), dx, dy);
				}
			}
			lastX = x;
			lastY = y;
		}
		for (int b = 0; b < 3; b++) {
			boolean now = msg.get("b" + b).getAsBoolean();
			if (now != screenButtons[b]) {
				mouse.valcraft$onButton(handle, new MouseButtonInfo(b, mods), now ? 1 : 0);
				screenButtons[b] = now;
			}
		}
		if (msg.has("scroll")) mouse.valcraft$onScroll(handle, 0, msg.get("scroll").getAsDouble());
	}

	private void press(KeyMapping mapping, JsonObject keys, String name) {
		if (!keys.has(name)) return;
		boolean now = keys.get(name).getAsBoolean();
		boolean was = down.getOrDefault(name, false);
		down.put(name, now);
		mapping.setDown(now);
		// Attack, use and drop act on "clicks", which setDown alone doesn't produce.
		if (now && !was) KeyMapping.click(KeyMappingHelper.getBoundKeyOf(mapping));
	}
}
