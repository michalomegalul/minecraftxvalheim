package dev.michal.valcraft.mixin;

import dev.michal.valcraft.CollisionField;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.EntityGetter;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

import java.util.ArrayList;
import java.util.List;

/**
 * Players also collide with Valheim's objects. Entity.collide (movement and step-up) and the
 * noCollision checks (crouch, pose, spawning) all gather "entity collisions" here.
 */
@Mixin(EntityGetter.class)
public interface EntityGetterMixin {
	@Inject(method = "getEntityCollisions", at = @At("RETURN"), cancellable = true)
	private void valcraft$addValheimObjects(Entity entity, AABB area, CallbackInfoReturnable<List<VoxelShape>> cir) {
		if (!(entity instanceof Player) || CollisionField.size() == 0) return;
		List<VoxelShape> extra = new ArrayList<>();
		CollisionField.collect(area, extra);
		if (extra.isEmpty()) return;
		extra.addAll(cir.getReturnValue());
		cir.setReturnValue(extra);
	}
}
