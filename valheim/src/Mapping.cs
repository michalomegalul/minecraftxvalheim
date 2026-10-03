using System;

namespace Valcraft
{
    /// <summary>
    /// Fixed 1:1 mapping between Valheim and Minecraft coordinates (1 metre = 1 block).
    ///
    /// Minecraft is +X east, +Z south; Unity is +X right, +Z forward. Negating Z keeps the world
    /// un-mirrored, which makes Unity yaw = Minecraft yaw + 180. Valheim's sea level (30) sits at
    /// Minecraft y = -10, leaving room for mountains under Minecraft's height limit (320).
    /// Unity-free so it can be unit tested.
    /// </summary>
    internal static class Mapping
    {
        public const double YOffset = 40;

        public static (double x, double y, double z) ToMc(double vx, double vy, double vz) => (vx, vy - YOffset, -vz);

        public static (double x, double y, double z) ToValheim(double mx, double my, double mz) => (mx, my + YOffset, -mz);

        public static float ToUnityYaw(float mcYaw) => mcYaw + 180f;

        public static float ToMcYaw(float unityYaw)
        {
            float y = (unityYaw - 180f) % 360f;
            return y < -180f ? y + 360f : y >= 180f ? y - 360f : y;
        }

        /// <summary>Centre of Minecraft block (bx, by, bz), in Valheim coordinates.</summary>
        public static (double x, double y, double z) BlockCenterInValheim(int bx, int by, int bz) =>
            ToValheim(bx + 0.5, by + 0.5, bz + 0.5);

        public static int ChunkOf(double mcCoord) => (int)Math.Floor(Math.Floor(mcCoord) / 16.0);
    }
}
