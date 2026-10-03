package dev.michal.valcraft;

import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.block.state.BlockState;

/** Entry points for mixins (which can only reach public classes) into package-private Terrain. */
public final class TerrainHooks {
	private TerrainHooks() {
	}

	/** Is this the snow layer we put on top of a Valheim ground column? */
	public static boolean isValheimGroundTop(BlockPos pos) {
		Terrain t = Terrain.instance;
		if (t == null || !ValcraftClient.isLinked()) return false;
		Double surface = t.surfaceAt(pos.getX(), pos.getZ());
		return surface != null && pos.getY() == (int) Math.floor(surface);
	}

	/** Server thread, before any block change in the world. */
	public static void beforeSetBlock(ServerLevel level, BlockPos pos, BlockState newState) {
		Terrain t = Terrain.instance;
		if (t == null || Terrain.writing || !ValcraftClient.isLinked()) return;
		if (newState.isAir() && Terrain.isGround(level.getBlockState(pos))) t.onGroundRemoved(pos);
		if (!t.isWritten(Math.floorDiv(pos.getX(), 16), Math.floorDiv(pos.getZ(), 16))) return;
		// Re-mirror the section, and neighbours when on an edge (their faces may now show).
		int x = pos.getX(), y = pos.getY(), z = pos.getZ();
		for (int dx = -1; dx <= 1; dx++)
			for (int dy = -1; dy <= 1; dy++)
				for (int dz = -1; dz <= 1; dz++)
					BlockMirror.markDirty(Math.floorDiv(x + dx, 16), Math.floorDiv(y + dy, 16), Math.floorDiv(z + dz, 16));
	}
}
