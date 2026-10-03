package dev.michal.valcraft;

import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;

/** Entry points for mixins (which can only reach public classes) into package-private Terrain. */
public final class TerrainHooks {
	private TerrainHooks() {
	}

	public static void beforeRemove(ServerLevel level, BlockPos pos) {
		Terrain t = Terrain.instance;
		if (t == null || Terrain.writing || !ValcraftClient.isLinked()) return;
		if (Terrain.isGround(level.getBlockState(pos))) t.onGroundRemoved(pos);
	}
}
