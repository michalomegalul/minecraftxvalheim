package dev.michal.valcraft;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.mojang.blaze3d.platform.TextureUtil;
import com.mojang.blaze3d.vertex.VertexConsumer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.model.geom.builders.UVPair;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.renderer.block.FluidRenderer;
import net.minecraft.client.renderer.block.ModelBlockRenderer;
import net.minecraft.client.renderer.chunk.ChunkSectionLayer;
import net.minecraft.client.renderer.texture.TextureAtlas;
import net.minecraft.core.BlockPos;
import net.minecraft.core.SectionPos;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.RenderShape;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.material.FluidState;
import org.joml.Vector3fc;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Base64;
import java.util.EnumMap;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.function.Consumer;

/**
 * Shows Minecraft's blocks in Valheim: every dirty 16x16x16 section is rendered with Minecraft's
 * own block and fluid renderers into captured quads (positions, atlas UVs, per-vertex colour),
 * which Valheim turns into meshes textured with Minecraft's block atlas. So stairs, glass,
 * torches, water and lava look like Minecraft, lit by Valheim.
 *
 * Valheim's own ground (our terrain blocks at or below the surface) is skipped: Valheim draws it.
 * Client thread only, except {@link #markDirty} which the server thread calls too.
 */
final class BlockMirror {
	private static final int SECTIONS_PER_TICK = 4;
	/** Wait this many ticks after a change, so the server's block update reached the client. */
	private static final int SETTLE_TICKS = 2;

	private static final Map<Long, Integer> dirty = new ConcurrentHashMap<>();
	private final Consumer<JsonObject> send;
	private final Terrain terrain;
	private long tick;

	private Path atlasFile;
	private long atlasRequestedAt = -1;
	private boolean atlasSent;

	BlockMirror(Consumer<JsonObject> send, Terrain terrain) {
		this.send = send;
		this.terrain = terrain;
	}

	static void markDirty(int sx, int sy, int sz) {
		dirty.putIfAbsent(SectionPos.asLong(sx, sy, sz), 0);
	}

	static void markColumnDirty(int cx, int cz, int minY, int maxY) {
		for (int sy = Math.floorDiv(minY, 16); sy <= Math.floorDiv(maxY, 16); sy++) markDirty(cx, sy, cz);
	}

	void reset() {
		dirty.clear();
		atlasSent = false;
		atlasRequestedAt = -1;
	}

	void tick(Minecraft mc) {
		tick++;
		ClientLevel level = mc.level;
		if (level == null) return;
		sendAtlas(mc);

		int budget = SECTIONS_PER_TICK;
		for (var e : dirty.entrySet()) {
			if (budget == 0) break;
			if (e.getValue() == 0) {
				e.setValue((int) tick); // first seen now; give the client time to receive the change
				continue;
			}
			if (tick - e.getValue() < SETTLE_TICKS) continue;
			long key = e.getKey();
			int sx = SectionPos.x(key), sy = SectionPos.y(key), sz = SectionPos.z(key);
			if (!level.hasChunk(sx, sz)) continue;
			dirty.remove(key);
			send.accept(compile(mc, level, sx, sy, sz));
			budget--;
		}
	}

	/** Export the block atlas from the GPU once and send it as a PNG. */
	private void sendAtlas(Minecraft mc) {
		if (atlasSent) return;
		try {
			if (atlasRequestedAt < 0 || tick - atlasRequestedAt > 200) {
				Path dir = mc.gameDirectory.toPath().resolve("valcraft");
				Files.createDirectories(dir);
				atlasFile = dir.resolve("blocks_0.png");
				Files.deleteIfExists(atlasFile);
				TextureAtlas atlas = (TextureAtlas) mc.getTextureManager().getTexture(TextureAtlas.LOCATION_BLOCKS);
				TextureUtil.writeAsPNG(dir, "blocks", atlas.getTexture(), 0, argb -> argb); // async GPU readback
				atlasRequestedAt = tick;
				return;
			}
			if (tick - atlasRequestedAt < 5 || !Files.exists(atlasFile)) return;
			byte[] png = Files.readAllBytes(atlasFile);
			JsonObject msg = new JsonObject();
			msg.addProperty("t", "atlas");
			msg.addProperty("png", Base64.getEncoder().encodeToString(png));
			send.accept(msg);
			atlasSent = true;
		} catch (Exception e) {
			atlasRequestedAt = tick; // try again later
		}
	}

	/** Is this one of our Valheim-ground blocks (drawn by Valheim, not by us)? */
	private boolean isValheimGround(BlockState state, BlockPos pos) {
		if (state.is(Blocks.BEDROCK)) return true;
		if (!Terrain.isGround(state)) return false;
		Double surface = terrain.surfaceAt(pos.getX(), pos.getZ());
		return surface != null && pos.getY() <= Math.floor(surface);
	}

	private JsonObject compile(Minecraft mc, ClientLevel level, int sx, int sy, int sz) {
		Map<ChunkSectionLayer, Quads> layers = new EnumMap<>(ChunkSectionLayer.class);
		ModelBlockRenderer blocks = new ModelBlockRenderer(true, true, mc.getBlockColors());
		FluidRenderer fluids = new FluidRenderer(mc.getModelManager().getFluidStateModelSet());
		var modelSet = mc.getModelManager().getBlockStateModelSet();
		FluidRenderer.Output fluidOut = layer -> layers.computeIfAbsent(layer, l -> new Quads());

		// Light sources (torches, lava, glowstone...), merged per 4x4x4 cell: [sumX, sumY, sumZ, count, maxLevel].
		Map<Integer, float[]> lights = new java.util.HashMap<>();
		BlockPos min = SectionPos.of(sx, sy, sz).origin();
		for (BlockPos pos : BlockPos.betweenClosed(min, min.offset(15, 15, 15))) {
			BlockState state = level.getBlockState(pos);
			if (state.isAir() || isValheimGround(state, pos) || Terrain.isSea(state, pos)) continue;
			int emission = state.getLightEmission();
			if (emission > 0) {
				int rx = pos.getX() - min.getX(), ry = pos.getY() - min.getY(), rz = pos.getZ() - min.getZ();
				float[] l = lights.computeIfAbsent((rx >> 2) | (ry >> 2) << 2 | (rz >> 2) << 4, k -> new float[5]);
				l[0] += rx + 0.5f;
				l[1] += ry + 0.5f;
				l[2] += rz + 0.5f;
				l[3]++;
				l[4] = Math.max(l[4], emission);
			}
			FluidState fluid = state.getFluidState();
			if (!fluid.isEmpty()) fluids.tesselate(level, pos, fluidOut, state, fluid);
			if (state.getRenderShape() == RenderShape.MODEL) {
				blocks.tesselateBlock(
						(x, y, z, quad, inst) -> {
							Quads q = layers.computeIfAbsent(quad.materialInfo().layer(), l -> new Quads());
							for (int v = 0; v < 4; v++) {
								Vector3fc p = quad.position(v);
								long uv = quad.packedUV(v);
								q.vertex(x + p.x(), y + p.y(), z + p.z(), UVPair.unpackU(uv), UVPair.unpackV(uv), inst.getColor(v));
							}
						},
						SectionPos.sectionRelative(pos.getX()), SectionPos.sectionRelative(pos.getY()), SectionPos.sectionRelative(pos.getZ()),
						level, pos, state, modelSet.get(state), state.getSeed(pos));
			}
		}

		JsonObject msg = new JsonObject();
		msg.addProperty("t", "mesh");
		msg.addProperty("sx", sx);
		msg.addProperty("sy", sy);
		msg.addProperty("sz", sz);
		JsonObject out = new JsonObject();
		layers.forEach((layer, q) -> {
			if (q.count > 0) out.add(layer.name().toLowerCase(), q.toJson());
		});
		msg.add("layers", out);
		JsonArray lightArr = new JsonArray();
		for (float[] l : lights.values()) {
			lightArr.add(l[0] / l[3]);
			lightArr.add(l[1] / l[3]);
			lightArr.add(l[2] / l[3]);
			lightArr.add(l[4]);
		}
		msg.add("lights", lightArr);
		return msg;
	}

	/** Render one block's model at the origin (for entities like lit TNT): layers JSON, same format. */
	static JsonObject tesselateSingle(Minecraft mc, ClientLevel level, BlockState state, BlockPos at) {
		Map<ChunkSectionLayer, Quads> layers = new EnumMap<>(ChunkSectionLayer.class);
		ModelBlockRenderer blocks = new ModelBlockRenderer(false, true, mc.getBlockColors());
		if (state.getRenderShape() == RenderShape.MODEL) {
			blocks.tesselateBlock(
					(x, y, z, quad, inst) -> {
						Quads q = layers.computeIfAbsent(quad.materialInfo().layer(), l -> new Quads());
						for (int v = 0; v < 4; v++) {
							Vector3fc p = quad.position(v);
							long uv = quad.packedUV(v);
							q.vertex(p.x(), p.y(), p.z(), UVPair.unpackU(uv), UVPair.unpackV(uv), inst.getColor(v));
						}
					},
					0, 0, 0, level, at, state, mc.getModelManager().getBlockStateModelSet().get(state), 42L);
		}
		JsonObject out = new JsonObject();
		layers.forEach((layer, q) -> {
			if (q.count > 0) out.add(layer.name().toLowerCase(), q.toJson());
		});
		return out;
	}

	/** Captured quads (4 vertices each), as flat arrays. Also used as the fluid VertexConsumer. */
	static final class Quads implements VertexConsumer {
		final JsonArray pos = new JsonArray(), uv = new JsonArray(), color = new JsonArray();
		int count;

		void vertex(float x, float y, float z, float u, float v, int argb) {
			addVertex(x, y, z);
			setUv(u, v);
			setColor(argb);
		}

		JsonObject toJson() {
			JsonObject o = new JsonObject();
			o.add("p", pos);
			o.add("uv", uv);
			o.add("c", color);
			return o;
		}

		private static float round(float f) {
			return Math.round(f * 10000f) / 10000f;
		}

		@Override
		public VertexConsumer addVertex(float x, float y, float z) {
			pos.add(round(x));
			pos.add(round(y));
			pos.add(round(z));
			count++;
			// Keep the arrays aligned even if the renderer skips colour or UV for a vertex.
			if (uv.size() < (count - 1) * 2) {
				uv.add(0f);
				uv.add(0f);
			}
			if (color.size() < count - 1) color.add(-1);
			return this;
		}

		@Override
		public VertexConsumer setColor(int r, int g, int b, int a) {
			return setColor((a & 255) << 24 | (r & 255) << 16 | (g & 255) << 8 | (b & 255));
		}

		@Override
		public VertexConsumer setColor(int argb) {
			if (color.size() < count) color.add(argb);
			return this;
		}

		@Override
		public VertexConsumer setUv(float u, float v) {
			if (uv.size() < count * 2) {
				uv.add(u);
				uv.add(v);
			}
			return this;
		}

		@Override
		public VertexConsumer setUv1(int u, int v) {
			return this;
		}

		@Override
		public VertexConsumer setUv2(int u, int v) {
			return this;
		}

		@Override
		public VertexConsumer setNormal(float x, float y, float z) {
			return this;
		}

		@Override
		public VertexConsumer setLineWidth(float width) {
			return this;
		}
	}
}
