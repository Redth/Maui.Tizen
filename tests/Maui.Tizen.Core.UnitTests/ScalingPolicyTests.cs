using System;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// The single display-scaling policy, exercised over injected metrics.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These pin the policy against <c>Tizen.UIExtensions.Common.DeviceInfo</c>, which is what the
	/// NUI controls, gesture recognisers and swipe thresholds use. The backend previously computed
	/// <c>Window.Default.Dpi.X / 160</c>, which disagreed with it in three separate ways; the
	/// expected values below are taken from DeviceInfo's own algorithm rather than from what this
	/// backend happens to produce.
	/// </para>
	/// <para>
	/// On a device the backend delegates to DeviceInfo directly, so these tests guard the
	/// specification - if Samsung changes the algorithm, the mismatch shows up here rather than as
	/// controls that no longer line up with their layout.
	/// </para>
	/// </remarks>
	[Collection(DisplayDensityCollection.Name)]
	public class ScalingPolicyTests
	{
		[Theory]
		[InlineData(true, false)]
		[InlineData(false, true)]
		[InlineData(true, true)]
		public void TvAndIoTUseAFixedDpiRegardlessOfWhatThePanelReports(bool isTv, bool isIoT)
		{
			// The most consequential divergence. On TV and IoT, UIExtensions never reads the panel
			// - it returns 213. Reading the reported DPI there put the backend on a completely
			// different scale from every native control on the same screen.
			Assert.Equal(
				TizenScalingPolicy.TvAndIoTDpi,
				TizenScalingPolicy.ResolveDpi(reportedDpiY: 320, isTv: isTv, isIoT: isIoT));
		}

		[Fact]
		public void OtherProfilesUseThePanelsReportedDpi()
		{
			Assert.Equal(320, TizenScalingPolicy.ResolveDpi(reportedDpiY: 320, isTv: false, isIoT: false));
		}

		[Fact]
		public void TheFixedTvDpiIs213()
		{
			// Pinned as a value, not just as a symbol: it is a Samsung constant this backend has to
			// agree with exactly, and a plausible-looking edit to it would otherwise go unnoticed.
			Assert.Equal(213, TizenScalingPolicy.TvAndIoTDpi);
		}

		[Fact]
		public void PixelModeDisablesScaling()
		{
			var metrics = new TizenDisplayMetrics(320, 1080, 1920, TizenDisplayResolutionUnit.Pixel);

			Assert.Equal(1.0, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void DpModeIsDpiOver160()
		{
			var metrics = new TizenDisplayMetrics(320, 1080, 1920, TizenDisplayResolutionUnit.DP);

			Assert.Equal(2.0, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void VpModeDividesTheScreenIntoAFixedNumberOfUnits()
		{
			var metrics = new TizenDisplayMetrics(320, 1080, 1920, TizenDisplayResolutionUnit.VP, ViewPortWidth: 360);

			Assert.Equal(3.0, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void VpModeFallsBackWhenNoViewportIsSet()
		{
			// ViewPortWidth defaults to 0, and dividing by it would be an infinity that quietly
			// propagates into every measurement. DeviceInfo guards the same way.
			var metrics = new TizenDisplayMetrics(320, 1080, 1920, TizenDisplayResolutionUnit.VP, ViewPortWidth: 0);

			Assert.Equal(1.0, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Theory]
		// Portrait DP size <= 1000: no multiplier.
		[InlineData(320, 1080, 1920, 1.0)]
		// 2400px at 160dpi = 2400dp wide, 4000dp tall -> portrait size 2400 -> x4.
		[InlineData(160, 2400, 4000, 4.0)]
		// 1600px at 160dpi = 1600dp -> between 1000 and 2000 -> x2.5.
		[InlineData(160, 1600, 2000, 2.5)]
		public void DeviceScaledPixelModeMultipliesOnLargeDisplays(
			int dpi, int width, int height, double expected)
		{
			var metrics = new TizenDisplayMetrics(dpi, width, height, TizenDisplayResolutionUnit.DeviceScaledPixel);

			Assert.Equal(expected, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void DeviceScaledDpModeIsBothScaledAndMultiplied()
		{
			// DeviceScaledDP satisfies BOTH branches of DeviceInfo's algorithm: it takes DPI / 160
			// and then the large-display multiplier. Treating the modes as mutually exclusive - the
			// obvious reading - gets this one wrong.
			//
			// 160 dpi -> physical scale 1.0; 1600dp portrait -> x2.5.
			var metrics = new TizenDisplayMetrics(160, 1600, 2000, TizenDisplayResolutionUnit.DeviceScaledDP);

			Assert.Equal(2.5, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void DeviceScaledDpAppliesTheMultiplierOnTopOfANonUnitScale()
		{
			// 320 dpi -> physical scale 2.0. 3200px / 2.0 = 1600dp portrait -> x2.5. So 5.0.
			var metrics = new TizenDisplayMetrics(320, 3200, 4000, TizenDisplayResolutionUnit.DeviceScaledDP);

			Assert.Equal(5.0, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void LargeDisplayThresholdsAreMeasuredInDpNotPixels()
		{
			// A high-density phone has plenty of PIXELS but few DP, and must not be treated as a
			// large display. 2160px at 480dpi is 720dp - comfortably under the threshold - so
			// comparing raw pixels here would wrongly quadruple everything on an ordinary handset.
			var metrics = new TizenDisplayMetrics(480, 2160, 3840, TizenDisplayResolutionUnit.DeviceScaledPixel);

			Assert.Equal(1.0, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void PhysicalScaleIgnoresTheDisplayUnit()
		{
			// The distinction the backend had collapsed: ToPixel is physical and ToScaledPixel is
			// scaled, and they coincide only in DP mode.
			Assert.Equal(2.0, TizenScalingPolicy.PhysicalScale(320));

			var pixelMode = new TizenDisplayMetrics(320, 1080, 1920, TizenDisplayResolutionUnit.Pixel);

			Assert.Equal(1.0, TizenScalingPolicy.ComputeScalingFactor(pixelMode));
			Assert.NotEqual(TizenScalingPolicy.PhysicalScale(320), TizenScalingPolicy.ComputeScalingFactor(pixelMode));
		}

		[Fact]
		public void TvScalingIsDrivenByTheFixedDpiEndToEnd()
		{
			// The whole point, in one assertion: a TV reporting 96 dpi still scales by 213/160,
			// because that is what the native controls next to it do.
			var dpi = TizenScalingPolicy.ResolveDpi(reportedDpiY: 96, isTv: true, isIoT: false);
			var metrics = new TizenDisplayMetrics(dpi, 1920, 1080, TizenDisplayResolutionUnit.DP);

			Assert.Equal(213 / 160.0, TizenScalingPolicy.ComputeScalingFactor(metrics));
		}

		[Fact]
		public void TheHostFallbackIsUnscaled()
		{
			// Off-device there is no window to ask, and returning anything other than 1.0 would
			// silently scale every host-side layout calculation.
			TizenDisplayDensity.SetDensityOverride(null);

			try
			{
				Assert.Equal(1.0, TizenDisplayDensity.Current);
				Assert.Equal(1.0, TizenDisplayDensity.PhysicalScale);
			}
			finally
			{
				TizenDisplayDensity.SetDensityOverride(null);
			}
		}

		[Fact]
		public void TheDensityOverrideDrivesBothConversions()
		{
			TizenDisplayDensity.SetDensityOverride(2.5);

			try
			{
				Assert.Equal(2.5, TizenDisplayDensity.Current);
				Assert.Equal(25, 10.0.ToScaledPixel());
				Assert.Equal(4.0, 10.0.ToScaledDP());
			}
			finally
			{
				TizenDisplayDensity.SetDensityOverride(null);
			}
		}
	}
}
