package dev.michal.valcraft.mixin;

import dev.michal.valcraft.TerrainHooks;
import net.minecraft.world.item.context.BlockPlaceContext;
import net.minecraft.world.level.block.SnowLayerBlock;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * The top of Valheim's ground is a snow layer (for 1/8-block heights). Let anything placed on the
 * ground replace it, so torches and blocks can be put straight onto Valheim terrain.
 */
@Mixin(SnowLayerBlock.class)
public abstract class SnowLayerBlockMixin {
	@Inject(method = "canBeReplaced", at = @At("HEAD"), cancellable = true)
	private void valcraft$replaceValheimGround(BlockState state, BlockPlaceContext context, CallbackInfoReturnable<Boolean> cir) {
		if (TerrainHooks.isValheimGroundTop(context.getClickedPos())) cir.setReturnValue(true);
	}
}
