package dev.michal.valcraft.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import dev.michal.valcraft.ValcraftClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Minecraft drops to 10 FPS when minimized or "AFK" (no real input for a minute). Its ticks run
 * inside the frame loop, so at 10 FPS they arrive in bursts and Valheim's movement stutters.
 * Unthrottled, a hidden Minecraft competes with Valheim for the GPU and Valheim flickers.
 * So while linked, run at the player's FPS limit capped to 60: ticks stay even, GPU use stays low.
 */
@Mixin(FramerateLimitTracker.class)
public class FramerateLimitTrackerMixin {
	private static final int LINKED_FPS_CAP = 60;

	@Shadow
	private int framerateLimit;

	@Inject(method = "getFramerateLimit", at = @At("HEAD"), cancellable = true)
	private void valcraft$steadyFpsWhileLinked(CallbackInfoReturnable<Integer> cir) {
		if (ValcraftClient.isLinked()) {
			cir.setReturnValue(Math.min(framerateLimit, LINKED_FPS_CAP));
		}
	}
}
