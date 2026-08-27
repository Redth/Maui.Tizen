using System;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Behavioural tests for the property decisions that a device-only mapping would otherwise hide.
	/// </summary>
	public class PropertyResolverTests
	{
		static int ToScaledPixel(double value) => (int)Math.Round(value * 2);

		[Fact]
		public void CombinedDecorationsArePreserved()
		{
			// The regression. TextDecorations is a [Flags] enum and the mapping switched over the
			// whole value, so Underline|Strikethrough matched neither arm and fell through to
			// None - silently removing BOTH decorations in the case where the user asked for the
			// most. Nothing threw, and the ref-pack lane compiles a switch just as happily.
			var resolved = TizenPropertyResolvers.ResolveTextDecorations(
				TextDecorations.Underline | TextDecorations.Strikethrough);

			Assert.Equal(
				TizenPropertyResolvers.UnderlineDecoration | TizenPropertyResolvers.StrikethroughDecoration,
				resolved);
		}

		[Theory]
		[InlineData(TextDecorations.None, TizenPropertyResolvers.NoDecorations)]
		[InlineData(TextDecorations.Underline, TizenPropertyResolvers.UnderlineDecoration)]
		[InlineData(TextDecorations.Strikethrough, TizenPropertyResolvers.StrikethroughDecoration)]
		public void SingleDecorationsMapToTheirNativeBit(TextDecorations decorations, int expected) =>
			Assert.Equal(expected, TizenPropertyResolvers.ResolveTextDecorations(decorations));

		[Fact]
		public void ClearingAMinimumResetsTheNativeConstraint()
		{
			// The regression. An unset minimum returned early instead of writing 0, so whatever
			// minimum had been applied previously stayed on the native view forever and the view
			// could never shrink below it again.
			Assert.Equal(0, TizenPropertyResolvers.ResolveMinimum(Dimension.Unset, ToScaledPixel));
			Assert.Equal(0, TizenPropertyResolvers.ResolveMinimum(double.NaN, ToScaledPixel));
		}

		[Fact]
		public void AnExplicitMinimumIsScaled()
		{
			Assert.Equal(20, TizenPropertyResolvers.ResolveMinimum(10, ToScaledPixel));
			Assert.Equal(0, TizenPropertyResolvers.ResolveMinimum(0, ToScaledPixel));
		}

		[Fact]
		public void MinimumTransitionsFromSetToClearedGoBackToZero()
		{
			// The sequence that actually bit: set a minimum, then clear it. Testing only the
			// cleared case in isolation would pass even if the mapping ignored clearing, because
			// "no constraint" and "constraint of zero" look identical from a single call.
			var applied = TizenPropertyResolvers.ResolveMinimum(44, ToScaledPixel);
			Assert.Equal(88, applied);

			var cleared = TizenPropertyResolvers.ResolveMinimum(Dimension.Unset, ToScaledPixel);
			Assert.Equal(0, cleared);

			Assert.NotEqual(applied, cleared);
		}
	}
}
