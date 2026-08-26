namespace Maui.Tizen.Validation.Tests;

/// <summary>
/// Self-tests for the screenshot comparison stack.
/// </summary>
/// <remarks>
/// The baseline suite is only as trustworthy as its codec and comparer. These tests run on every
/// hosted build, with no device and no product code, so a regression in the comparison logic is
/// caught here rather than being mistaken for a rendering regression on a device.
/// </remarks>
public class ImageComparisonTests
{
    static readonly BaselineTolerance Exact = new() { MaxChannelDelta = 0, MaxDifferingPixelRatio = 0 };

    static PngImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var image = new PngImage(width, height, new byte[width * height * 4]);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                image.SetPixel(x, y, r, g, b, a);
        }

        return image;
    }

    [Fact]
    public void EncodeThenDecode_RoundTripsExactly()
    {
        var original = Solid(9, 7, 12, 200, 45, 128);
        original.SetPixel(0, 0, 255, 255, 255, 255);
        original.SetPixel(8, 6, 0, 0, 0, 0);

        var decoded = PngImage.Decode(original.Encode());

        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);
        Assert.Equal(original.Pixels, decoded.Pixels);
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        // Byte-identical output for identical input is what lets baselines be diffed and reviewed.
        var image = Solid(16, 16, 10, 20, 30);
        Assert.Equal(image.Encode(), image.Encode());
    }

    [Fact]
    public void Decode_RejectsNonPngData()
    {
        var ex = Assert.Throws<InvalidDataException>(() => PngImage.Decode([1, 2, 3, 4, 5, 6, 7, 8, 9]));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_IdenticalImages_Passes()
    {
        var image = Solid(8, 8, 1, 2, 3);
        var result = ImageComparer.Compare(image, PngImage.Decode(image.Encode()), Exact);

        Assert.True(result.Passed, result.Describe("identical"));
        Assert.Equal(0, result.DifferingPixels);
        Assert.Equal(0, result.MaxChannelDelta);
    }

    [Fact]
    public void Compare_DifferentSizes_FailsWithoutThrowing()
    {
        var result = ImageComparer.Compare(Solid(4, 4, 0, 0, 0), Solid(4, 5, 0, 0, 0), Exact);

        Assert.False(result.Passed);
        Assert.Contains("4x5", result.Describe("size"), StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_SinglePixelChange_IsDetectedAndLocated()
    {
        var expected = Solid(4, 4, 0, 0, 0);
        var actual = Solid(4, 4, 0, 0, 0);
        actual.SetPixel(2, 3, 255, 255, 255);

        var result = ImageComparer.Compare(expected, actual, Exact);

        Assert.False(result.Passed);
        Assert.Equal(1, result.DifferingPixels);
        Assert.Equal((2, 3), result.FirstDifference);
        Assert.Equal(255, result.MaxChannelDelta);
    }

    [Fact]
    public void Compare_ChannelDeltaWithinTolerance_Passes()
    {
        var expected = Solid(4, 4, 100, 100, 100);
        var actual = Solid(4, 4, 102, 100, 100);

        var tolerance = new BaselineTolerance { MaxChannelDelta = 2, MaxDifferingPixelRatio = 0 };
        var result = ImageComparer.Compare(expected, actual, tolerance);

        Assert.True(result.Passed, result.Describe("within-tolerance"));

        // The delta is still reported even though it was tolerated, so drift stays visible.
        Assert.Equal(2, result.MaxChannelDelta);
    }

    [Fact]
    public void Compare_ChannelDeltaAboveTolerance_Fails()
    {
        var expected = Solid(4, 4, 100, 100, 100);
        var actual = Solid(4, 4, 104, 100, 100);

        var tolerance = new BaselineTolerance { MaxChannelDelta = 2, MaxDifferingPixelRatio = 0 };

        Assert.False(ImageComparer.Compare(expected, actual, tolerance).Passed);
    }

    [Fact]
    public void Compare_DifferingPixelRatio_IsHonoured()
    {
        // 1 differing pixel out of 100 == 0.01.
        var expected = Solid(10, 10, 0, 0, 0);
        var actual = Solid(10, 10, 0, 0, 0);
        actual.SetPixel(5, 5, 255, 255, 255);

        var tooTight = new BaselineTolerance { MaxChannelDelta = 0, MaxDifferingPixelRatio = 0.005 };
        var justEnough = new BaselineTolerance { MaxChannelDelta = 0, MaxDifferingPixelRatio = 0.01 };

        Assert.False(ImageComparer.Compare(expected, actual, tooTight).Passed);
        Assert.True(ImageComparer.Compare(expected, actual, justEnough).Passed);
    }

    [Fact]
    public void WriteFailureArtifacts_ProducesInspectableOutput()
    {
        using var workspace = TempWorkspace.Create("baseline-artifacts");

        var expected = Solid(4, 4, 0, 0, 0);
        var actual = Solid(4, 4, 0, 0, 0);
        actual.SetPixel(1, 1, 255, 0, 0);

        var result = ImageComparer.Compare(expected, actual, Exact);
        var directory = ImageComparer.WriteFailureArtifacts(workspace.Path, "mobile/light/hdpi/button", expected, actual, result);

        Assert.True(File.Exists(Path.Combine(directory, "expected.png")));
        Assert.True(File.Exists(Path.Combine(directory, "actual.png")));
        Assert.True(File.Exists(Path.Combine(directory, "diff.png")));
        Assert.True(File.Exists(Path.Combine(directory, "summary.txt")));

        // The diff must itself be a valid PNG so CI can attach and render it.
        var diff = PngImage.Load(Path.Combine(directory, "diff.png"));
        Assert.Equal((255, 0, 255, 255), diff.GetPixel(1, 1));
    }
}
