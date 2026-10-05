using System;
using System.Globalization;

namespace BannerlordBlocks
{
    // No engine dependencies: timings are mission tick intervals, not GPU frame timings.
    internal sealed class TestDiagnostics
    {
        internal readonly string Id = Guid.NewGuid().ToString("N").Substring(0, 12);
        internal int Created, Deleted, Rejected, Selected, Errors, PeakBlocks;
        private int samples;
        private double elapsed, total, maximum;

        internal bool Sample(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return false;
            samples++;
            elapsed += dt;
            total += dt;
            maximum = Math.Max(maximum, dt);
            if (elapsed < 10) return false;
            elapsed = 0;
            return true;
        }

        internal string Summary(int blocks, int agents)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "session={0}; blocks={1}; peak={2}; agents={3}; created={4}; deleted={5}; rejected={6}; selected={7}; errors={8}; tickSamples={9}; meanTickMs={10:F2}; maxTickMs={11:F2}; managedMiB={12:F2}",
                Id, blocks, PeakBlocks, agents, Created, Deleted, Rejected, Selected, Errors,
                samples, samples == 0 ? 0 : total / samples * 1000, maximum * 1000,
                GC.GetTotalMemory(false) / 1048576.0);
        }
    }
}
