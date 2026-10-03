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
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.function.Consumer;

/**
 * Builds Valheim's terrain around the player into the Minecraft world, as blocks.
 * Minecraft is hidden, so the blocks never need to look right; they only need to collide right.
 *
 * Ground: full blocks up to the surface, topped with a snow layer whose collision height
 * ((layers - 1) / 8) matches Valheim's height to 1/8 block, so slopes walk smoothly (Minecraft can
 * only step up 0.6 blocks). Real blocks, so Minecraft tools and TNT can dig them.
 * Objects (rocks, trees, buildings) are not blocks: see {@link CollisionField}.
 *
 * Digging: when ground blocks disappear (pickaxe, shovel, TNT, creepers), the column's highest
 * remaining ground block is re-measured at the end of the tick, and a lower surface is sent to
 * Valheim, which digs its terrain to match. Tunnelling sideways leaves the top in place, so
 * Valheim's surface doesn't change.
 *
 * Runs on the integrated server thread with a per-tick budget.
 */
final class Terrain {
	/** Blocks of ground under the surface; Valheim can be dug 8 m down, bedrock below that. */
	private static final int DEPTH = 9;
	/** Cells above the surface that we clear (removes barriers left by older versions). */
	static final int CLEAR_HEIGHT = 12;
	private static final int CHUNKS_PER_TICK = 2;
	// UPDATE_SKIP_ON_PLACE keeps our water from scheduling a flow tick: it stays still (and doesn't
	// pour off the edge of the built area into the void) until something next to it changes.
	private static final int FLAGS = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SKIP_ON_PLACE;
	/** Valheim's sea level in Minecraft y (sent with each chunk); everything below it is water. */
	static volatile double seaLevel = Double.NaN;

	private final ConcurrentLinkedQueue<JsonObject> incoming = new ConcurrentLinkedQueue<>();
	private final ArrayDeque<JsonObject> waiting = new ArrayDeque<>(); // chunk not loaded yet
	private final Set<Long> written = java.util.concurrent.ConcurrentHashMap.newKeySet();
	// Server thread only: the surface we built per column, and columns that lost ground blocks.
	private final Map<Long, Double> surfaces = new java.util.concurrent.ConcurrentHashMap<>();
	private final Set<Long> dirty = java.util.concurrent.ConcurrentHashMap.newKeySet();
	private final Consumer<JsonObject> send;
	/** True while we write blocks ourselves, so our own changes don't count as digging. */
	static boolean writing;
	static Terrain instance;

	Terrain(Consumer<JsonObject> send) {
		this.send = send;
		instance = this;
	}

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
		surfaces.clear();
		dirty.clear();
	}

	/** The surface we built for this column, or null if it isn't Valheim ground. */
	Double surfaceAt(int x, int z) {
		return surfaces.get(BlockPos.asLong(x, 0, z));
	}

	/** Water we placed for Valheim's sea (Valheim draws its own ocean, so don't mirror it back). */
	static boolean isSea(BlockState state, BlockPos pos) {
		return !Double.isNaN(seaLevel) && pos.getY() < seaLevel && state.is(Blocks.WATER);
	}

	static boolean isGround(BlockState state) {
		return state.is(Blocks.DIRT) || state.is(Blocks.STONE) || state.is(Blocks.SNOW);
	}

	/** Server thread (from LevelMixin): a ground block at pos is about to be removed. */
	void onGroundRemoved(BlockPos pos) {
		long col = BlockPos.asLong(pos.getX(), 0, pos.getZ());
		Double surface = surfaces.get(col);
		if (surface != null && pos.getY() <= Math.floor(surface)) dirty.add(col);
	}

	/** Server thread, every tick. */
	void tick(ServerLevel level) {
		if (!dirty.isEmpty()) sendDigs(level);
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
			writing = true;
			try {
				write(level, chunk, cx, cz);
			} finally {
				writing = false;
			}
			// Re-mirror this chunk's blocks to Valheim (picks up blocks placed in earlier sessions).
			int[] range = surfaceRange(chunk);
			BlockMirror.markColumnDirty(cx, cz, range[0] - DEPTH, range[1] + 48);
			written.add(key(cx, cz));
			budget--;
		}
	}

	private static int[] surfaceRange(JsonObject chunk) {
		int lo = Integer.MAX_VALUE, hi = Integer.MIN_VALUE;
		for (var h : chunk.getAsJsonArray("top")) {
			if (h.isJsonNull()) continue;
			int y = (int) Math.floor(h.getAsDouble());
			lo = Math.min(lo, y);
			hi = Math.max(hi, y);
		}
		return lo > hi ? new int[]{0, 0} : new int[]{lo, hi};
	}

	/** Re-measure dug columns and tell Valheim about lowered surfaces. */
	private void sendDigs(ServerLevel level) {
		JsonArray cols = new JsonArray();
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		for (long col : dirty) {
			int x = BlockPos.getX(col), z = BlockPos.getZ(col);
			Double known = surfaces.get(col);
			if (known == null) continue;
			double old = known;
			int n = (int) Math.floor(old);
			double now = n - DEPTH; // everything dug out
			for (int y = n; y >= n - DEPTH; y--) {
				BlockState state = level.getBlockState(pos.set(x, y, z));
				if (!isGround(state)) continue;
				now = state.is(Blocks.SNOW) ? y + (state.getValue(SnowLayerBlock.LAYERS) - 1) / 8.0 : y + 1;
				break;
			}
			if (now < old - 0.01) {
				surfaces.put(col, now);
				cols.add(x);
				cols.add(z);
				cols.add(now);
			}
		}
		dirty.clear();
		if (cols.isEmpty()) return;
		JsonObject msg = new JsonObject();
		msg.addProperty("t", "dig");
		msg.add("cols", cols);
		send.accept(msg);
	}

	private void write(ServerLevel level, JsonObject chunk, int cx, int cz) {
		JsonArray top = chunk.getAsJsonArray("top");
		if (chunk.has("sea")) seaLevel = chunk.get("sea").getAsDouble();
		int sea = Double.isNaN(seaLevel) ? Integer.MIN_VALUE : (int) Math.round(seaLevel);
		BlockState water = Blocks.WATER.defaultBlockState();

		BlockState ground = Blocks.DIRT.defaultBlockState();
		BlockState deep = Blocks.STONE.defaultBlockState();
		BlockState air = Blocks.AIR.defaultBlockState();
		BlockState floor = Blocks.BEDROCK.defaultBlockState();
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		int minY = level.getMinY(), maxY = level.getMaxY();

		for (int lz = 0; lz < 16; lz++) {
			for (int lx = 0; lx < 16; lx++) {
				int x = cx * 16 + lx, z = cz * 16 + lz;
				var h = top.get(lx + lz * 16);
				if (h.isJsonNull()) continue; // Valheim had no terrain here (not loaded)
				double surface = h.getAsDouble();
				long col = BlockPos.asLong(x, 0, z);
				Double known = surfaces.get(col);
				// Unchanged columns are left alone, so rescans never wipe what the player built.
				if (known != null && Math.abs(known - surface) < 0.02) continue;
				surfaces.put(col, surface);
				int n = (int) Math.floor(surface);
				int layers = (int) Math.round((surface - n) * 8) + 1; // collision = (layers - 1) / 8

				for (int y = n - DEPTH; y <= Math.max(n + CLEAR_HEIGHT, sea - 1); y++) {
					if (y < minY || y > maxY) continue;
					pos.set(x, y, z);
					BlockState state;
					if (y == n - DEPTH) state = floor;
					else if (y < n - 1) state = deep;
					else if (y < n) state = ground;
					else if (y == n) state = layers >= 9 ? ground : layers <= 1 ? air
							: Blocks.SNOW.defaultBlockState().setValue(SnowLayerBlock.LAYERS, layers);
					else state = air;
					if (state.isAir() && y < sea) state = water; // Valheim's sea and rivers
					level.setBlock(pos, state, FLAGS);
				}
			}
		}
	}

	private static long key(int cx, int cz) {
		return ((long) cx << 32) ^ (cz & 0xffffffffL);
	}
}
