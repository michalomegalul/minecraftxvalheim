package dev.michal.valcraft;

import com.mojang.blaze3d.buffers.GpuBuffer;
import com.mojang.blaze3d.buffers.GpuBufferSlice;
import com.mojang.blaze3d.pipeline.RenderTarget;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.blaze3d.textures.GpuTexture;

import java.io.RandomAccessFile;
import java.nio.ByteOrder;
import java.nio.MappedByteBuffer;
import java.nio.channels.FileChannel;
import java.nio.file.Path;

/**
 * Streams Minecraft's hand and GUI (hotbar, hearts, inventory screens, chat) to Valheim, which
 * draws it over its own picture, like SkyCraft's hand and GUI layers.
 *
 * While linked, Minecraft skips drawing its world and clears to transparent (GameRendererMixin),
 * so its main render target holds just the hand and GUI. Each frame we copy it from the GPU to a
 * buffer, and when that's ready, into a shared-memory file both games map (/dev/shm). Layout:
 * <pre>
 *   0  int  magic 'VCGI'
 *   4  int  width
 *   8  int  height
 *  12  int  1 if rows are top-down (Vulkan), 0 if bottom-up (OpenGL)
 *  16  long frame counter: odd while writing, even when a frame is complete
 *  32  RGBA8 pixels
 * </pre>
 */
public final class GuiCapture {
	private static final int HEADER = 32;
	private static final int MAGIC = 0x49474356; // "VCGI" little-endian
	private static final Path FILE = Path.of("/dev/shm/valcraft_gui");

	private static GpuBuffer buffer;
	private static int width, height;
	private static volatile boolean copyDone;
	private static boolean copyPending;
	private static MappedByteBuffer shm;
	private static long frame;
	private static int topDown = -1;
	private static long lastCopyNanos;
	/** 30 frames a second is smooth for a GUI and halves the copying. */
	private static final long MIN_INTERVAL_NANOS = 33_000_000L;

	private GuiCapture() {
	}

	/** Render thread, after GameRenderer.render. */
	public static void afterRender(RenderTarget target) {
		if (!ValcraftClient.isLinked() || target.getColorTexture() == null) return;
		try {
			if (copyPending && copyDone) publish();
			long now = System.nanoTime();
			if (!copyPending && now - lastCopyNanos >= MIN_INTERVAL_NANOS) {
				lastCopyNanos = now;
				startCopy(target);
			}
		} catch (Exception e) {
			copyPending = false;
		}
	}

	private static void startCopy(RenderTarget target) {
		GpuTexture tex = target.getColorTexture();
		int w = tex.getWidth(0), h = tex.getHeight(0);
		var device = RenderSystem.getDevice();
		if (buffer == null || w != width || h != height) {
			if (buffer != null) buffer.close();
			buffer = device.createBuffer(() -> "Valcraft GUI capture", 9, (long) w * h * 4);
			width = w;
			height = h;
		}
		if (topDown < 0) topDown = device.getDeviceInfo().backendName().toLowerCase().contains("opengl") ? 0 : 1;
		copyDone = false;
		copyPending = true;
		device.createCommandEncoder().copyTextureToBuffer(tex, buffer, 0L, () -> copyDone = true, 0);
	}

	private static void publish() throws Exception {
		copyPending = false;
		long size = HEADER + (long) width * height * 4;
		if (shm == null || shm.capacity() < size) {
			try (RandomAccessFile f = new RandomAccessFile(FILE.toFile(), "rw")) {
				f.setLength(size);
				shm = f.getChannel().map(FileChannel.MapMode.READ_WRITE, 0, size);
				shm.order(ByteOrder.LITTLE_ENDIAN);
			}
		}
		shm.putLong(16, ++frame | 1); // odd: writing
		try (GpuBufferSlice.MappedView view = buffer.map(true, false)) {
			shm.put(HEADER, view.data(), 0, width * height * 4);
		}
		shm.putInt(0, MAGIC);
		shm.putInt(4, width);
		shm.putInt(8, height);
		shm.putInt(12, topDown);
		frame++;
		shm.putLong(16, frame & ~1L); // even: complete
	}
}
