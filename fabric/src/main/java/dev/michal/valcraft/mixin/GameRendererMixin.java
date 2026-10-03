package dev.michal.valcraft.mixin;

import com.llamalad7.mixinextras.injector.v2.WrapWithCondition;
import dev.michal.valcraft.GuiCapture;
import dev.michal.valcraft.ValcraftClient;
import net.minecraft.client.renderer.GameRenderer;
import net.minecraft.client.renderer.LevelRenderer;
import org.joml.Vector4f;
import org.joml.Vector4fc;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyArg;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * While linked, Valheim draws the world: Minecraft only renders the hand and GUI, over a
 * transparent background, and hands the frame to GuiCapture.
 */
@Mixin(GameRenderer.class)
public abstract class GameRendererMixin {
	private static final Vector4fc TRANSPARENT = new Vector4f(0, 0, 0, 0);

	@Shadow
	public abstract com.mojang.blaze3d.pipeline.RenderTarget mainRenderTarget();

	@ModifyArg(method = "render", index = 1, at = @At(value = "INVOKE",
			target = "Lcom/mojang/blaze3d/systems/CommandEncoder;clearColorAndDepthTextures(Lcom/mojang/blaze3d/textures/GpuTexture;Lorg/joml/Vector4fc;Lcom/mojang/blaze3d/textures/GpuTexture;D)V"))
	private Vector4fc valcraft$transparentClear(Vector4fc color) {
		return ValcraftClient.isLinked() ? TRANSPARENT : color;
	}

	@WrapWithCondition(method = "renderLevel", at = @At(value = "INVOKE",
			target = "Lnet/minecraft/client/renderer/LevelRenderer;render(Lcom/mojang/blaze3d/resource/GraphicsResourceAllocator;Lnet/minecraft/client/DeltaTracker;ZLnet/minecraft/client/renderer/state/level/CameraRenderState;Lorg/joml/Matrix4fc;Lcom/mojang/blaze3d/buffers/GpuBufferSlice;Lorg/joml/Vector4f;Z)V"))
	private boolean valcraft$skipWorld(LevelRenderer renderer, com.mojang.blaze3d.resource.GraphicsResourceAllocator a,
			net.minecraft.client.DeltaTracker b, boolean c, net.minecraft.client.renderer.state.level.CameraRenderState d,
			org.joml.Matrix4fc e, com.mojang.blaze3d.buffers.GpuBufferSlice f, Vector4f g, boolean h) {
		return !ValcraftClient.isLinked();
	}

	@Inject(method = "render", at = @At("TAIL"))
	private void valcraft$capture(net.minecraft.client.DeltaTracker deltaTracker, boolean advanceGameTime, CallbackInfo ci) {
		GuiCapture.afterRender(mainRenderTarget());
	}
}
