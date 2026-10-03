using System.Collections.Generic;
using Valcraft;
using Xunit;

public class VoxelizeTests
{
    [Fact]
    public void PairsTopAndBottomPerCollider()
    {
        // A 0.5-high box from y=10.0 to 10.5, and a beam from 12.25 to 12.5.
        var spans = Voxelize.Spans(new[] { (1, 10.5), (2, 12.5) }, new[] { (1, 10.0), (2, 12.25) }, 9, 23);
        Assert.Equal(new List<(int, int)> { (80, 84), (98, 100) }, spans);
    }

    [Fact]
    public void ColliderBeyondRayRangeExtendsToTheEnd()
    {
        // Seen only from above: reaches below the range (an embedded rock).
        Assert.Equal(new List<(int, int)> { (72, 84) }, Voxelize.Spans(new[] { (1, 10.5) }, new (int, double)[0], 9, 23));
        // Seen only from below: reaches above the range (a tall cliff).
        Assert.Equal(new List<(int, int)> { (160, 184) }, Voxelize.Spans(new (int, double)[0], new[] { (1, 20.0) }, 9, 23));
    }

    [Fact]
    public void ThinThingsNeverVanish()
    {
        // A 2 cm plank still becomes at least one 1/8 slice.
        var (lo, hi) = Voxelize.Quantize(10.01, 10.03);
        Assert.True(hi > lo);
        Assert.True(lo <= 80 && hi >= 81);
    }

    [Fact]
    public void MergesOverlapsAndTouches()
    {
        var merged = Voxelize.Merge(new List<(int, int)> { (10, 20), (5, 12), (20, 25), (30, 31) });
        Assert.Equal(new List<(int, int)> { (5, 25), (30, 31) }, merged);
    }

    [Fact]
    public void JoinsRunsAlongX()
    {
        var grid = new List<(int, int)>[8, 8];
        // A wall 2/8 thick (sz = 3,4) across the whole block, 2 m high from y8 = 80.
        for (int sx = 0; sx < 8; sx++)
        {
            grid[sx, 3] = new List<(int, int)> { (80, 96) };
            grid[sx, 4] = new List<(int, int)> { (80, 96) };
        }
        // A stair step in one corner.
        grid[0, 0] = new List<(int, int)> { (80, 81) };
        var output = new List<int>();
        Voxelize.EmitBoxes(-2, 5, grid, output);
        Assert.Equal(new List<int>
        {
            -16, -15, 40, 80, 81,   // corner step
            -16, -8, 43, 80, 96,    // wall row sz=3, joined across all 8
            -16, -8, 44, 80, 96,    // wall row sz=4
        }, output);
    }
}
