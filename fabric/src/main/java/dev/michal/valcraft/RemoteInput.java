package dev.michal.valcraft;

import com.google.gson.JsonObject;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Options;
import net.minecraft.client.player.LocalPlayer;

import java.util.HashMap;
import java.util.Map;

/** Applies keyboard/mouse input forwarded from the Valheim window. */
final class RemoteInput {
	private final Map<String, Boolean> down = new HashMap<>();

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
		}
		// Valheim owns the look direction (so its camera has no lag); we just follow it.
		if (msg.has("yaw")) {
			player.setYRot(msg.get("yaw").getAsFloat());
			player.setXRot(msg.get("pitch").getAsFloat());
		}
		if (msg.has("slot")) {
			player.getInventory().setSelectedSlot(msg.get("slot").getAsInt());
		}
		if (msg.has("scroll")) {
			int slot = player.getInventory().getSelectedSlot() - msg.get("scroll").getAsInt();
			player.getInventory().setSelectedSlot(Math.floorMod(slot, 9));
		}
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
