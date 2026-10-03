package dev.michal.valcraft;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.SnowLayerBlock;
import net.minecraft.world.level.block.state.BlockState;

import java.util.ArrayDeque;
import java.util.Set;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * Builds Valheim's terrain around the player into the Minecraft world, as blocks.
 * Minecraft is hidden, so the blocks never need to look right; they only need to collide right.
 *
 * Ground: full blocks up to the surface, topped with a snow layer whose collision height
 * ((layers - 1) / 8) matches Valheim's height to 1/8 block, so slopes walk smoothly (Minecraft can
 * only step up 0.6 blocks). Real blocks, so Minecraft tools and TNT can dig them.
 * Objects (rocks, trees, buildings) are not blocks: see {@link CollisionField}.
 * Runs on the integrated server thread with a per-tick budget.
 */
final class Terrain {
	/** Blocks of ground under the surface; anything deeper is never reachable. */
	private static final int DEPTH = 4;
	/** Cells above the surface that we clear (removes barriers left by older versions). */
	static final int CLEAR_HEIGHT = 12;
	private static final int CHUNKS_PER_TICK = 2;
	private static final int FLAGS = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE;

	private final ConcurrentLinkedQueue<JsonObject> incoming = new ConcurrentLinkedQueue<>();
	private final ArrayDeque<JsonObject> waiting = new ArrayDeque<>(); // chunk not loaded yet
	private final Set<Long> written = java.util.concurrent.ConcurrentHashMap.newKeySet();

	/** Called from the client thread when a chunk arrives from Valheim. */
	void enqueue(JsonObject chunk) {
		incoming.add(chunk);
	}

	boolean isWritten(int cx, int cz) {
		return written.contains(key(cx, cz));
	}

	void clear() {
		incoming.clear();
		written.clear();
	}

	/** Server thread, every tick. */
	void tick(ServerLevel level) {
		JsonObject c;
		while ((c = incoming.poll()) != null) waiting.add(c);
		int budget = CHUNKS_PER_TICK;
		int tries = waiting.size();
		while (budget > 0 && tries-- > 0) {
			JsonObject chunk = waiting.poll();
			if (!chunk.has("top")) continue; // object-only rescan
			int cx = chunk.get("cx").getAsInt(), cz = chunk.get("cz").getAsInt();
			if (!level.hasChunk(cx, cz)) {
				waiting.add(chunk); // the server loads chunks near the player; try again later
				continue;
			}
			write(level, chunk, cx, cz);
			written.add(key(cx, cz));
			budget--;
		}
	}

	private static void write(ServerLevel level, JsonObject chunk, int cx, int cz) {
		JsonArray top = chunk.getAsJsonArray("top");

		BlockState ground = Blocks.DIRT.defaultBlockState();
		BlockState deep = Blocks.STONE.defaultBlockState();
		BlockState air = Blocks.AIR.defaultBlockState();
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		int minY = level.getMinY(), maxY = level.getMaxY();

		for (int lz = 0; lz < 16; lz++) {
			for (int lx = 0; lx < 16; lx++) {
				int x = cx * 16 + lx, z = cz * 16 + lz;
				var h = top.get(lx + lz * 16);
				if (h.isJsonNull()) continue; // Valheim had no terrain here (not loaded)
				double surface = h.getAsDouble();
				int n = (int) Math.floor(surface);
				int layers = (int) Math.round((surface - n) * 8) + 1; // collision = (layers - 1) / 8

				for (int y = n - DEPTH; y <= n + CLEAR_HEIGHT; y++) {
					if (y < minY || y > maxY) continue;
					pos.set(x, y, z);
					BlockState state;
					if (y < n - 1) state = deep;
					else if (y < n) state = ground;
					else if (y == n) state = layers >= 9 ? ground : layers <= 1 ? air
							: Blocks.SNOW.defaultBlockState().setValue(SnowLayerBlock.LAYERS, layers);
					else state = air;
					level.setBlock(pos, state, FLAGS);
				}
			}
		}
	}

	private static long key(int cx, int cz) {
		return ((long) cx << 32) ^ (cz & 0xffffffffL);
	}
}
