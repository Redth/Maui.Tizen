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

		[Theory]
		// Microsoft.Maui ordinal -> native Tizen.UIExtensions ordinal. Both read from metadata.
		[InlineData(LineBreakMode.NoWrap, TizenPropertyResolvers.NoWrapLineBreak)]
		[InlineData(LineBreakMode.WordWrap, TizenPropertyResolvers.WordWrapLineBreak)]
		[InlineData(LineBreakMode.CharacterWrap, TizenPropertyResolvers.CharacterWrapLineBreak)]
		[InlineData(LineBreakMode.HeadTruncation, TizenPropertyResolvers.HeadTruncationLineBreak)]
		[InlineData(LineBreakMode.TailTruncation, TizenPropertyResolvers.TailTruncationLineBreak)]
		[InlineData(LineBreakMode.MiddleTruncation, TizenPropertyResolvers.MiddleTruncationLineBreak)]
		public void EveryLineBreakModeMapsToItsNativeCounterpart(LineBreakMode mode, int expected) =>
			Assert.Equal(expected, TizenPropertyResolvers.ResolveLineBreakMode(mode));

		[Fact]
		public void LineBreakModesAreNotOrdinalCompatibleSoCastingIsWrong()
		{
			// The reason a conversion table exists at all. Microsoft.Maui's NoWrap is 0 while the
			// native NoWrap is 1, so a straight cast turns NoWrap into None and shifts every value
			// after it. Nothing throws; labels just wrap wrongly.
			Assert.NotEqual((int)LineBreakMode.NoWrap, TizenPropertyResolvers.ResolveLineBreakMode(LineBreakMode.NoWrap));

			// Spelled out for the one that silently becomes "None".
			Assert.Equal(0, (int)LineBreakMode.NoWrap);
			Assert.Equal(TizenPropertyResolvers.NoneLineBreak, 0);
			Assert.Equal(1, TizenPropertyResolvers.ResolveLineBreakMode(LineBreakMode.NoWrap));
		}

		[Fact]
		public void EveryDeclaredLineBreakModeIsMappedExplicitly()
		{
			// Guards against a new enum member silently landing on the WordWrap default arm.
			foreach (var mode in Enum.GetValues<LineBreakMode>())
			{
				var resolved = TizenPropertyResolvers.ResolveLineBreakMode(mode);

				Assert.InRange(resolved, TizenPropertyResolvers.NoWrapLineBreak, TizenPropertyResolvers.TailTruncationLineBreak);
			}

			// Six members today; if that changes, the table above needs revisiting.
			Assert.Equal(6, Enum.GetValues<LineBreakMode>().Length);
		}

		[Fact]
		public void ExclusionWinsOverIsInAccessibleTree()
		{
			// The overwrite hazard, resolved in one place. Both annotations write to BOTH NUI flags,
			// so applying them through separate helpers let whichever mapper key ran last undo the
			// other - an element excluded with its children became reachable again purely because
			// IsInAccessibleTree happened to be mapped afterwards.
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(
				isInAccessibleTree: true, excludedWithChildren: true);

			Assert.True(hidden);
			Assert.False(highlightable);
		}

		[Theory]
		[InlineData(true, false, true)]
		[InlineData(false, true, false)]
		public void IsInAccessibleTreeDrivesBothFlagsWhenNotExcluded(
			bool inTree, bool expectedHidden, bool expectedHighlightable)
		{
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(inTree, null);

			Assert.Equal(expectedHidden, hidden);
			Assert.Equal(expectedHighlightable, highlightable);
		}

		[Fact]
		public void UnannotatedViewsStayReachable()
		{
			// Neither annotation set must not hide anything; that is NUI's own default and the
			// overwhelmingly common case.
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(null, null);

			Assert.False(hidden);
			Assert.True(highlightable);
		}

		[Fact]
		public void ExplicitlyNotExcludedDoesNotForceReachability()
		{
			// ExcludedWithChildren=false is not a statement about reachability, so it must not
			// override an explicit IsInAccessibleTree=false.
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(
				isInAccessibleTree: false, excludedWithChildren: false);

			Assert.True(hidden);
			Assert.False(highlightable);
		}

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
