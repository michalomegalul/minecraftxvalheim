package dev.michal.valcraft;

import com.google.gson.JsonArray;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;

import java.util.List;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Valheim's objects (rocks, trees, buildings, stairs) as fine collision boxes, 1/8 block
 * resolution, the way SkyCraft does it. Not blocks: these are fed straight into Minecraft's
 * collision queries for players (see EntityGetterMixin), so a 20 cm wall stays 20 cm and
 * stairs become 1/8-block steps that Minecraft's step-up walks.
 *
 * Shared by the client and the integrated server (same JVM), so both agree on collisions.
 */
public final class CollisionField {
	private static final double SUB = 1 / 8.0;
	private static final ConcurrentHashMap<Long, AABB[]> CHUNKS = new ConcurrentHashMap<>();

	private CollisionField() {
	}

	/** Boxes for one chunk: flat ints [x8Start, x8End, z8, y8Bottom, y8Top]... in 1/8 blocks. */
	static void put(int cx, int cz, JsonArray flat) {
		AABB[] boxes = new AABB[flat.size() / 5];
		for (int i = 0, b = 0; b < boxes.length; i += 5, b++) {
			int x0 = flat.get(i).getAsInt(), x1 = flat.get(i + 1).getAsInt(), z = flat.get(i + 2).getAsInt();
			int y0 = flat.get(i + 3).getAsInt(), y1 = flat.get(i + 4).getAsInt();
			boxes[b] = new AABB(x0 * SUB, y0 * SUB, z * SUB, x1 * SUB, y1 * SUB, (z + 1) * SUB);
		}
		CHUNKS.put(key(cx, cz), boxes);
	}

	static void clear() {
		CHUNKS.clear();
	}

	public static int size() {
		return CHUNKS.size();
	}

	/** Adds every box intersecting {@code area} to {@code out}. */
	public static void collect(AABB area, List<VoxelShape> out) {
		if (CHUNKS.isEmpty()) return;
		int cx0 = Math.floorDiv((int) Math.floor(area.minX), 16), cx1 = Math.floorDiv((int) Math.floor(area.maxX), 16);
		int cz0 = Math.floorDiv((int) Math.floor(area.minZ), 16), cz1 = Math.floorDiv((int) Math.floor(area.maxZ), 16);
		for (int cx = cx0; cx <= cx1; cx++) {
			for (int cz = cz0; cz <= cz1; cz++) {
				AABB[] boxes = CHUNKS.get(key(cx, cz));
				if (boxes == null) continue;
				for (AABB b : boxes) {
					if (b.intersects(area)) out.add(Shapes.create(b));
				}
			}
		}
	}

	private static long key(int cx, int cz) {
		return ((long) cx << 32) ^ (cz & 0xffffffffL);
	}
}
