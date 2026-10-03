using System;
using Valcraft;
using Xunit;

public class MappingTests
{
    [Fact]
    public void RoundTrips()
    {
        var mc = Mapping.ToMc(123.25, 47.5, -88.75);
        Assert.Equal((123.25, 7.5, 88.75), mc);
        Assert.Equal((123.25, 47.5, -88.75), Mapping.ToValheim(mc.x, mc.y, mc.z));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(-90f)]
    [InlineData(179f)]
    public void FacingDirectionMatches(float mcYaw)
    {
        // Minecraft forward for yaw φ: (-sin φ, cos φ) in (x, z).
        double r = mcYaw * Math.PI / 180;
        var mcForward = (x: -Math.Sin(r), z: Math.Cos(r));
        var vForward = Mapping.ToValheim(mcForward.x, 0, mcForward.z);
        // Unity forward for yaw θ: (sin θ, cos θ) in (x, z).
        double t = Mapping.ToUnityYaw(mcYaw) * Math.PI / 180;
        Assert.Equal(Math.Sin(t), vForward.x, 6);
        Assert.Equal(Math.Cos(t), vForward.z, 6);
        Assert.Equal(mcYaw, Mapping.ToMcYaw(Mapping.ToUnityYaw(mcYaw)), 3);
    }

    [Fact]
    public void ChunksHandleNegatives()
    {
        Assert.Equal(0, Mapping.ChunkOf(0));
        Assert.Equal(0, Mapping.ChunkOf(15.9));
        Assert.Equal(1, Mapping.ChunkOf(16));
        Assert.Equal(-1, Mapping.ChunkOf(-0.1));
        Assert.Equal(-1, Mapping.ChunkOf(-16));
        Assert.Equal(-2, Mapping.ChunkOf(-16.1));
    }

    [Fact]
    public void BlockCentreLandsInsideTheBlock()
    {
        var c = Mapping.BlockCenterInValheim(-3, 10, 5);
        var back = Mapping.ToMc(c.x, c.y, c.z);
        Assert.Equal(-3, (int)Math.Floor(back.x));
        Assert.Equal(10, (int)Math.Floor(back.y));
        Assert.Equal(5, (int)Math.Floor(back.z));
    }
}
