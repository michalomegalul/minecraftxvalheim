package dev.michal.valcraft.mixin;

import net.minecraft.client.MouseHandler;
import net.minecraft.client.input.MouseButtonInfo;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

/** Feed Valheim's mouse into Minecraft's own handler, so screens get drags, double-clicks, tooltips. */
@Mixin(MouseHandler.class)
public interface MouseHandlerAccessor {
	@Invoker("onMove")
	void valcraft$onMove(long window, double x, double y);

	@Invoker("onButton")
	void valcraft$onButton(long window, MouseButtonInfo button, int action);

	@Invoker("onScroll")
	void valcraft$onScroll(long window, double x, double y);
}
