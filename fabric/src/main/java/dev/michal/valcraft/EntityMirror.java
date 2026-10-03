package dev.michal.valcraft;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.item.FallingBlockEntity;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.item.PrimedTnt;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;

import java.util.HashSet;
import java.util.Set;
import java.util.function.Consumer;

/**
 * Entities that are really blocks: lit TNT, falling sand/gravel, and dropped block items. Each
 * block's model is rendered once (at a spot high in the empty sky, so no faces are culled) and
 * sent as an "emodel"; then every tick their positions go out as "entities".
 */
final class EntityMirror {
	private static final double RANGE = 64;
	private final Consumer<JsonObject> send;
	private final Set<Integer> modelsSent = new HashSet<>();
	private boolean hadAny;

	EntityMirror(Consumer<JsonObject> send) {
		this.send = send;
	}

	void reset() {
		modelsSent.clear();
		hadAny = false;
	}

	void tick(Minecraft mc) {
		ClientLevel level = mc.level;
		if (level == null || mc.player == null) return;
		JsonArray list = new JsonArray();
		for (Entity e : level.entitiesForRendering()) {
			if (e.distanceToSqr(mc.player) > RANGE * RANGE) continue;
			BlockState state;
			float scale = 1f;
			int flash = 0;
			if (e instanceof PrimedTnt tnt) {
				state = tnt.getBlockState();
				flash = tnt.getFuse() / 5 % 2 == 0 ? 1 : 0; // Minecraft's white blink
			} else if (e instanceof FallingBlockEntity falling) {
				state = falling.getBlockState();
			} else if (e instanceof ItemEntity item && item.getItem().getItem() instanceof BlockItem bi) {
				state = bi.getBlock().defaultBlockState();
				scale = 0.25f;
			} else {
				continue;
			}
			int key = Block.getId(state);
			if (modelsSent.add(key)) {
				BlockPos sky = BlockPos.containing(mc.player.getX(), level.getMaxY() - 2, mc.player.getZ());
				JsonObject model = new JsonObject();
				model.addProperty("t", "emodel");
				model.addProperty("key", key);
				model.add("layers", BlockMirror.tesselateSingle(mc, level, state, sky));
				send.accept(model);
			}
			list.add(e.getId());
			list.add(e.getX());
			list.add(e.getY());
			list.add(e.getZ());
			list.add(key);
			list.add(scale);
			list.add(flash);
		}
		if (list.isEmpty() && !hadAny) return;
		hadAny = !list.isEmpty();
		JsonObject msg = new JsonObject();
		msg.addProperty("t", "entities");
		msg.add("list", list);
		send.accept(msg);
	}
}
