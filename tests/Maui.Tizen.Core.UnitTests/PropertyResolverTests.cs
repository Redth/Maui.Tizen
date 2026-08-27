using System;
using System.Linq;
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
		public void ExcludingOnlyTheParentLeavesDescendantsAccessible()
		{
			// The distinction that matters. NUI has two flags and they are not interchangeable:
			//
			//   AccessibilityHighlightable - can THIS element be reached
			//   AccessibilityHidden        - removes this element AND its whole subtree
			//
			// IsInAccessibleTree is a statement about one element, so mapping it onto Hidden took
			// every descendant with it - a container marked not-in-tree silently removed children
			// that carry no annotation and are individually accessible everywhere else.
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(
				isInAccessibleTree: false, excludedWithChildren: null);

			Assert.False(highlightable);

			// Crucially NOT true: the subtree stays reachable.
			Assert.NotEqual(true, hidden);
		}

		[Fact]
		public void ExcludedWithChildrenHidesTheWholeSubtree()
		{
			// The annotation that genuinely means "and children" is the only one that may set
			// Hidden.
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(
				isInAccessibleTree: null, excludedWithChildren: true);

			Assert.Equal(true, hidden);

			// An excluded subtree cannot have a reachable root.
			Assert.Equal(false, highlightable);
		}

		[Fact]
		public void UnsetAnnotationsLeaveNativeDefaultsAlone()
		{
			// Null means "do not write". Writing false unconditionally stamped over whatever the
			// control or the platform had already configured for an element nobody annotated.
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(null, null);

			Assert.Null(hidden);
			Assert.Null(highlightable);
		}

		[Fact]
		public void AnElementMayBeExplicitlyPlacedInTheAccessibleTree()
		{
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(
				isInAccessibleTree: true, excludedWithChildren: null);

			Assert.Equal(true, highlightable);
			Assert.Null(hidden);
		}

		[Fact]
		public void ExplicitlyNotExcludedDoesNotForceReachability()
		{
			// ExcludedWithChildren=false says nothing about this element's own reachability, so it
			// must not override an explicit IsInAccessibleTree=false.
			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(
				isInAccessibleTree: false, excludedWithChildren: false);

			Assert.Equal(false, hidden);
			Assert.Equal(false, highlightable);
		}

		[Theory]
		[InlineData(LineBreakMode.HeadTruncation, TizenPropertyResolvers.EllipsisAtStart)]
		[InlineData(LineBreakMode.MiddleTruncation, TizenPropertyResolvers.EllipsisAtMiddle)]
		[InlineData(LineBreakMode.TailTruncation, TizenPropertyResolvers.EllipsisAtEnd)]
		public void TruncationModesPlaceTheEllipsisCorrectly(LineBreakMode mode, int expected)
		{
			// UIExtensions collapses all three into `MultiLine = false; Ellipsis = true;` and never
			// touches EllipsisPosition, which defaults to End - so head and middle truncation both
			// rendered as tail truncation. Asking for a leading ellipsis produced a trailing one.
			Assert.Equal(expected, TizenPropertyResolvers.ResolveEllipsisPosition(mode));
		}

		[Theory]
		[InlineData(LineBreakMode.NoWrap)]
		[InlineData(LineBreakMode.WordWrap)]
		[InlineData(LineBreakMode.CharacterWrap)]
		public void NonTruncatingModesLeaveTheEllipsisPositionAlone(LineBreakMode mode) =>
			Assert.Null(TizenPropertyResolvers.ResolveEllipsisPosition(mode));

		[Fact]
		public void TheThreeTruncationPositionsAreDistinct()
		{
			// Guards against a table that compiles and looks right while mapping everything to the
			// same value - which is precisely the upstream behaviour being corrected.
			var positions = new[]
			{
				TizenPropertyResolvers.ResolveEllipsisPosition(LineBreakMode.HeadTruncation),
				TizenPropertyResolvers.ResolveEllipsisPosition(LineBreakMode.MiddleTruncation),
				TizenPropertyResolvers.ResolveEllipsisPosition(LineBreakMode.TailTruncation),
			};

			Assert.Equal(3, positions.Distinct().Count());
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

		[Fact]
		public void AMeasurePendingAfterTheOutermostPassMustBeRescheduled()
		{
			// The regression. A measure invalidated DURING a layout pass suppresses RequestLayout
			// to avoid re-entering the pass already running - so unless it is replayed as the
			// outermost pass exits, the invalidation is silently dropped and the content keeps a
			// stale measurement until some unrelated pass comes along.
			Assert.True(TizenPropertyResolvers.ShouldScheduleLayout(layoutDepth: 0, needMeasureUpdate: true));
		}

		[Fact]
		public void NoRescheduleWhileAPassIsStillRunning()
		{
			// Nested groups: only the outermost exit may schedule, or each level would queue its
			// own pass.
			Assert.False(TizenPropertyResolvers.ShouldScheduleLayout(layoutDepth: 1, needMeasureUpdate: true));
			Assert.False(TizenPropertyResolvers.ShouldScheduleLayout(layoutDepth: 3, needMeasureUpdate: true));
		}

		[Fact]
		public void NoRescheduleWhenNothingIsPending()
		{
			// A pass that invalidated nothing must not schedule another, or layout never settles.
			Assert.False(TizenPropertyResolvers.ShouldScheduleLayout(layoutDepth: 0, needMeasureUpdate: false));
		}
	}
}
