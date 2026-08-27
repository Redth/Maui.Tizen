namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards that Wave B consumes a single display-density policy rather than doing its own maths.
/// </summary>
/// <remarks>
/// <para>
/// Density conversion has to be identical across layout, gestures and native controls: if one
/// call site divides by <c>DPI.X</c> and another by a hard-coded 213, hit testing drifts away from
/// rendering and the bug is close to impossible to localise from a screenshot.
/// </para>
/// <para>
/// The policy itself lives in core's <c>TizenDisplayDensity</c> and is deliberately NOT duplicated
/// here. Wave B's own conversions are thin forwarders — the <c>float</c> overloads the core slice
/// does not expose — and this test fails if any Wave B source starts computing density itself.
/// </para>
/// </remarks>
public class DensityPolicyTests
{
	static readonly string[] BannedPatterns =
	{
		// Raw axis reads: DPI.X and DPI.Y disagree on non-square-pixel displays.
		".Dpi.X",
		".Dpi.Y",
		"DPI.X",
		"DPI.Y",

		// The Tizen TV convention, which belongs in the shared policy and nowhere else.
		"213",

		// The DP baseline: dividing by it here would fork the policy.
		"160.0",
		"/ 160",
	};

	[Fact]
	public void NoWaveBSourceComputesItsOwnDensity()
	{
		var offenders = new List<string>();

		foreach (var file in WaveBSource.Files)
		{
			var text = File.ReadAllText(file);

			foreach (var pattern in BannedPatterns)
			{
				if (text.Contains(pattern, StringComparison.Ordinal))
					offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)} contains '{pattern}'.");
			}
		}

		Assert.Empty(offenders);
	}

	/// <summary>
	/// Wave B's density helpers must forward to the shared policy, not reimplement it.
	/// </summary>
	[Fact]
	public void WaveBDensityHelpersForwardToTheSharedPolicy()
	{
		var interop = File.ReadAllText(
			RepoPaths.Combine("src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenWaveBInterop.cs"));

		// The float overloads exist only because the core slice offers int and double. They must
		// delegate rather than divide.
		Assert.Contains("ToScaledPixel()", interop, StringComparison.Ordinal);
		Assert.Contains("ToScaledDP()", interop, StringComparison.Ordinal);
	}
}
