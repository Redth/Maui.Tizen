namespace Maui.Tizen.TestUtils;

/// <summary>
/// Deterministic pixel comparison for screenshot baselines.
/// </summary>
/// <remarks>
/// The comparison is intentionally simple and total: same dimensions, per-channel absolute delta,
/// and a cap on how many pixels may differ at all. There is no perceptual metric and no
/// resampling, because both make failures hard to reason about and both drift between library
/// versions, silently changing the meaning of every checked-in baseline.
/// </remarks>
public static class ImageComparer
{
    /// <summary>Compares two images under <paramref name="tolerance"/>.</summary>
    public static ImageComparisonResult Compare(PngImage expected, PngImage actual, BaselineTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(tolerance);

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return ImageComparisonResult.SizeMismatch(
                (expected.Width, expected.Height),
                (actual.Width, actual.Height),
                CreateSizeMismatchDiff(expected, actual));
        }

        static PngImage CreateSizeMismatchDiff(PngImage expected, PngImage actual)
        {
            var width = Math.Max(expected.Width, actual.Width);
            var height = Math.Max(expected.Height, actual.Height);
            var diff = new PngImage(width, height, new byte[width * height * 4]);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (x >= expected.Width || y >= expected.Height || x >= actual.Width || y >= actual.Height)
                    {
                        diff.SetPixel(x, y, 255, 0, 255, 255);
                        continue;
                    }

                    var e = expected.GetPixel(x, y);
                    var a = actual.GetPixel(x, y);
                    if (e != a)
                    {
                        diff.SetPixel(x, y, 255, 0, 255, 255);
                        continue;
                    }

                    var dim = (byte)(((e.R + e.G + e.B) / 3) / 4);
                    diff.SetPixel(x, y, dim, dim, dim, 255);
                }
            }

            return diff;
        }

        var totalPixels = expected.Width * expected.Height;
        var differingPixels = 0;
        var maxChannelDelta = 0;
        var firstDifference = ((int X, int Y)?)null;

        var diff = new PngImage(expected.Width, expected.Height, new byte[totalPixels * 4]);

        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var e = expected.GetPixel(x, y);
                var a = actual.GetPixel(x, y);

                var delta = Math.Max(
                    Math.Max(Math.Abs(e.R - a.R), Math.Abs(e.G - a.G)),
                    Math.Max(Math.Abs(e.B - a.B), Math.Abs(e.A - a.A)));

                if (delta > maxChannelDelta)
                    maxChannelDelta = delta;

                if (delta > tolerance.MaxChannelDelta)
                {
                    differingPixels++;
                    firstDifference ??= (x, y);

                    // Magenta marks a real difference; intensity is not meaningful, presence is.
                    diff.SetPixel(x, y, 255, 0, 255, 255);
                }
                else
                {
                    // Matching regions are dimmed so the diff mask stays readable as an image.
                    var dim = (byte)(((e.R + e.G + e.B) / 3) / 4);
                    diff.SetPixel(x, y, dim, dim, dim, 255);
                }
            }
        }

        var ratio = totalPixels == 0 ? 0 : (double)differingPixels / totalPixels;
        var passed = ratio <= tolerance.MaxDifferingPixelRatio;

        return new ImageComparisonResult(
            passed,
            null,
            differingPixels,
            totalPixels,
            ratio,
            maxChannelDelta,
            firstDifference,
            diff);
    }

    /// <summary>
    /// Writes <c>expected</c>, <c>actual</c> and <c>diff</c> images into an artifact directory so a
    /// CI failure can be inspected without reproducing the run locally.
    /// </summary>
    /// <returns>The directory the artifacts were written to.</returns>
    public static string WriteFailureArtifacts(
        string artifactRoot,
        string caseName,
        PngImage expected,
        PngImage actual,
        ImageComparisonResult result)
    {
        var directory = Path.Combine(artifactRoot, caseName.Replace('/', '_'));
        Directory.CreateDirectory(directory);

        expected.Save(Path.Combine(directory, "expected.png"));
        actual.Save(Path.Combine(directory, "actual.png"));
        result.Diff?.Save(Path.Combine(directory, "diff.png"));

        File.WriteAllText(Path.Combine(directory, "summary.txt"), result.Describe(caseName));
        return directory;
    }
}

/// <param name="Diff">Diff mask on a canvas large enough to contain both images.</param>
public sealed record ImageComparisonResult(
    bool Passed,
    string? SizeMismatchDescription,
    int DifferingPixels,
    int TotalPixels,
    double DifferingPixelRatio,
    int MaxChannelDelta,
    (int X, int Y)? FirstDifference,
    PngImage? Diff)
{
    internal static ImageComparisonResult SizeMismatch(
        (int Width, int Height) expected,
        (int Width, int Height) actual,
        PngImage diff) =>
        new(
            false,
            $"expected {expected.Width}x{expected.Height} but captured {actual.Width}x{actual.Height}",
            0, 0, 0, 0, null, diff);

    /// <summary>Failure text naming the tolerance that was exceeded and where.</summary>
    public string Describe(string caseName)
    {
        if (SizeMismatchDescription is not null)
            return $"Baseline '{caseName}' size mismatch: {SizeMismatchDescription}.";

        if (Passed)
        {
            return $"Baseline '{caseName}' matched " +
                   $"({DifferingPixels}/{TotalPixels} pixels differ, max channel delta {MaxChannelDelta}).";
        }

        var location = FirstDifference is { } p ? $" First difference at ({p.X}, {p.Y})." : string.Empty;

        return
            $"Baseline '{caseName}' differs: {DifferingPixels}/{TotalPixels} pixels " +
            $"({DifferingPixelRatio:P4}) exceed the per-channel tolerance; " +
            $"max channel delta {MaxChannelDelta}.{location}";
    }
}
