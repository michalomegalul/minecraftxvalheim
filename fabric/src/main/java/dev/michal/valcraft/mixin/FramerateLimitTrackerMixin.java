package dev.michal.valcraft.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import dev.michal.valcraft.ValcraftClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Minecraft drops to 10 FPS when minimized or "AFK" (no real input for a minute). Its ticks run
 * inside the frame loop, so at 10 FPS they arrive in bursts and Valheim's movement stutters.
 * While linked we're never really idle, so never throttle.
 */
@Mixin(FramerateLimitTracker.class)
public class FramerateLimitTrackerMixin {
	@Inject(method = "getThrottleReason", at = @At("HEAD"), cancellable = true)
	private void valcraft$noThrottleWhileLinked(CallbackInfoReturnable<FramerateLimitTracker.FramerateThrottleReason> cir) {
		if (ValcraftClient.isLinked()) {
			cir.setReturnValue(FramerateLimitTracker.FramerateThrottleReason.NONE);
		}
	}
}
