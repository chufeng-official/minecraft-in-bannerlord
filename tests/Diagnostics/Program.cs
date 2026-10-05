using System;
using BannerlordBlocks;

internal static class Program
{
    private static void Main()
    {
        var first = new TestDiagnostics();
        var second = new TestDiagnostics();
        Check(first.Id != second.Id, "distinct session IDs");
        Check(!first.Sample(float.NaN) && !first.Sample(float.PositiveInfinity) &&
            !first.Sample(0) && !first.Sample(-1), "invalid samples ignored");
        Check(first.Summary(0, 0).Contains("tickSamples=0; meanTickMs=0.00; maxTickMs=0.00"), "empty summary");
        for (int i = 0; i < 9; i++) Check(!first.Sample(1), "sample budget");
        Check(first.Sample(1), "ten second report");
        Check(!first.Sample(1), "report timer resets");
        first.Created = 10;
        first.Deleted = 4;
        first.PeakBlocks = 10;
        first.Rejected = 2;
        first.Selected = 3;
        first.Errors = 1;
        string summary = first.Summary(6, 20);
        Check(summary.Contains("blocks=6; peak=10; agents=20; created=10; deleted=4"), "counter summary");
        Check(summary.Contains("rejected=2; selected=3; errors=1"), "interaction and error counters");
        Check(summary.Contains("tickSamples=11; meanTickMs=1000.00; maxTickMs=1000.00"), "timing summary");
        Check(PlacementGeometry.CellCenter(-0.01f) == -0.5f, "negative cell floor");
        Check(PlacementGeometry.CellCenter(-1f) == -0.5f, "negative cell boundary");
        Check(PlacementGeometry.CellCenter(0f) == 0.5f, "zero cell");
        Check(PlacementGeometry.CellCenter(1.9f) == 1.5f, "positive cell");
        for (int axis = 0; axis < 3; axis++)
        {
            foreach (int sign in new[] { -1, 1 })
            {
                int actualAxis, actualSign;
                PlacementGeometry.FaceOffset(axis == 0 ? sign * 0.5f : 0,
                    axis == 1 ? sign * 0.5f : 0, axis == 2 ? sign * 0.5f : 0,
                    out actualAxis, out actualSign);
                Check(actualAxis == axis && actualSign == sign, "six block faces");
            }
        }
        int cornerAxis, cornerSign;
        PlacementGeometry.FaceOffset(0.5f, 0.5f, 0.5f, out cornerAxis, out cornerSign);
        Check(cornerAxis == 0 && cornerSign == 1, "deterministic edge/corner tie");
        bool invalidRejected = false;
        try { PlacementGeometry.CellCenter(float.NaN); }
        catch (ArgumentOutOfRangeException) { invalidRejected = true; }
        Check(invalidRejected, "invalid grid coordinate rejected");
        Check(Math.Abs(PlacementGeometry.GroundCenter(10f, 10f) - 10.5f) < 0.0001f, "ground anchor layer");
        Check(Math.Abs(PlacementGeometry.GroundCenter(10.03f, 10f) - 11.5f) < 0.0001f, "slope rounds to whole block");
        Check(Math.Abs(PlacementGeometry.GroundCenter(9.97f, 10f) - 10.5f) < 0.0001f, "lower ground shares layer");
        Check(Math.Abs(PlacementGeometry.GroundCenter(9f, 10f) - 9.5f) < 0.0001f, "negative layer");
        Check(Math.Abs(PlacementGeometry.GroundCenter(10.0001f, 10f) - 10.5f) < 0.0001f, "terrain precision tolerance");
        Console.WriteLine("PASS: session IDs, sample validation, report budget, counters and timing summary");
        Console.WriteLine("PASS: grid flooring, six face offsets, deterministic corner and invalid coordinate");
        Console.WriteLine("PASS: shared vertical grid, slopes, negative layers and precision tolerance");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
    }
}
