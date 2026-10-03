using System;
using System.Collections.Generic;
using Valcraft;
using Xunit;
using Xunit.Abstractions;

public class PlaybackTests
{
    private readonly ITestOutputHelper _out;
    public PlaybackTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Runs Valheim frames at ~60 FPS against a stream of (tick, arrival) samples and reports
    /// starvation after a warm-up, plus the largest per-frame speed deviation.
    /// </summary>
    private (int starved, double worstSpeed) Simulate(List<(long tick, double at)> samples, double seconds, double tps = 20, int seed = 1)
    {
        var rng = new Random(seed);
        var pb = new Playback();
        int next = 0, starvedAtWarmup = 0;
        double t = 0, lastPos = double.NaN, worst = 0;
        while (t < seconds)
        {
            double dt = 1.0 / 60 + (rng.NextDouble() - 0.5) * 0.004; // a little frame-time jitter
            t += dt;
            while (next < samples.Count && samples[next].at <= t)
            {
                pb.OnSample(samples[next].tick, samples[next].at);
                next++;
            }
            double pos = pb.Advance(dt);
            if (t > 5)
            {
                // playback speed relative to Minecraft's true tick rate
                if (!double.IsNaN(lastPos)) worst = Math.Max(worst, Math.Abs((pos - lastPos) / dt / tps - 1.0));
            }
            else starvedAtWarmup = pb.Starved;
            lastPos = pos;
        }
        _out.WriteLine($"starved={pb.Starved - starvedAtWarmup} worstSpeed={worst:P1} target={pb.TargetTicks:F2} jitter={pb.Jitter:F2} mcSpeed={pb.McSpeed:F3}");
        return (pb.Starved - starvedAtWarmup, worst);
    }

    private static List<(long, double)> Stream(double period, double seconds, Func<long, double> extraDelay = null)
    {
        var list = new List<(long, double)>();
        for (long tick = 1000; ; tick++)
        {
            double at = 0.1 + (tick - 1000) * period + (extraDelay?.Invoke(tick) ?? 0);
            if (at > seconds) break;
            list.Add((tick, at));
        }
        // Stable sort by arrival: TCP keeps order, so equal arrival times keep tick order.
        return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(list, x => x.Item2));
    }

    [Fact]
    public void PerfectTwentyTps()
    {
        var (starved, speed) = Simulate(Stream(0.05, 30), 30);
        Assert.Equal(0, starved);
        Assert.True(speed < 0.10, $"speed deviation {speed:P1}");
    }

    [Fact]
    public void MinecraftSlightlySlow()
    {
        // 19.4 ticks/s: the old fixed 20 Hz clock drifted into the newest sample here.
        var (starved, speed) = Simulate(Stream(1 / 19.4, 40), 40, 19.4);
        Assert.Equal(0, starved);
        Assert.True(speed < 0.10, $"speed deviation {speed:P1}");
    }

    [Fact]
    public void BurstsOfTwoTicks()
    {
        // 10 FPS throttling: two ticks delivered together every 100 ms.
        var (starved, speed) = Simulate(Stream(0.05, 30, tick => tick % 2 == 0 ? 0.05 : 0.0), 30);
        Assert.Equal(0, starved);
        Assert.True(speed < 0.10, $"speed deviation {speed:P1}");
    }

    [Fact]
    public void SlowAndBursty()
    {
        // Worst seen so far, combined: 18 ticks/s, delivered in pairs, plus some jitter.
        var rng = new Random(3);
        var (starved, speed) = Simulate(Stream(1 / 18.0, 40, tick => (tick % 2 == 0 ? 1 / 18.0 : 0.0) + rng.NextDouble() * 0.01), 40, 18);
        Assert.Equal(0, starved);
        Assert.True(speed < 0.10, $"speed deviation {speed:P1}");
    }

    [Fact]
    public void SixtyFpsFrameAligned()
    {
        // Minecraft capped at 60 FPS while linked: each tick goes out on the next frame boundary.
        double frame = 1 / 60.0;
        var (starved, speed) = Simulate(Stream(0.05, 30, tick =>
        {
            double at = 0.1 + (tick - 1000) * 0.05;
            return Math.Ceiling(at / frame) * frame - at;
        }), 30);
        Assert.Equal(0, starved);
        Assert.True(speed < 0.10, $"speed deviation {speed:P1}");
    }

    [Fact]
    public void RandomNetworkJitter()
    {
        var rng = new Random(7);
        var (starved, speed) = Simulate(Stream(0.05, 30, _ => rng.NextDouble() * 0.015), 30);
        Assert.Equal(0, starved);
        Assert.True(speed < 0.10, $"speed deviation {speed:P1}");
    }
}
