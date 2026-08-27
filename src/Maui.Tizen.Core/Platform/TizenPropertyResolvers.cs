using System;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The pure decisions behind a handful of platform property mappings, kept separate from the
	/// NUI calls that apply them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything in <c>TizenPlatformExtensions</c> needs a real native view, so it can only ever be
	/// compile-checked against the TizenFX reference assemblies - never executed. That is fine for
	/// a one-line property assignment, but several genuine bugs have hidden in the small amount of
	/// <em>logic</em> in front of those assignments, where a test would have caught them
	/// immediately:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// Clearing a minimum returned early instead of resetting the native constraint, so a view
	/// whose <c>MinimumWidth</c> was set once could never shrink below it again.
	/// </item>
	/// <item>
	/// <c>TextDecorations</c> is a <c>[Flags]</c> enum, but it was matched with a switch over the
	/// whole value, so <c>Underline | Strikethrough</c> hit neither arm and fell through to
	/// <c>None</c> - dropping both decorations in the one case where the user asked for the most.
	/// </item>
	/// </list>
	/// <para>
	/// Pulling that logic into this file makes it run on the host. The platform code keeps only the
	/// native assignment, which is what the ref-pack lane is good at checking.
	/// </para>
	/// <para>
	/// Internal, not public: this is an implementation detail that exists to be testable, and the
	/// unit-test lane compiles these sources directly, so it does not need to widen the package's
	/// public surface.
	/// </para>
	/// </remarks>
	internal static class TizenPropertyResolvers
	{
		/// <summary>Bit values of <c>Tizen.UIExtensions.Common.TextDecorations</c>.</summary>
		/// <remarks>
		/// Mirrored as integers so this file stays free of TizenFX. Verified against the shipped
		/// assembly's metadata: None = 0, Underline = 1, Strikethrough = 2.
		/// </remarks>
		public const int NoDecorations = 0;

		/// <summary>The native <c>Underline</c> bit.</summary>
		public const int UnderlineDecoration = 1;

		/// <summary>The native <c>Strikethrough</c> bit.</summary>
		public const int StrikethroughDecoration = 2;

		/// <summary>
		/// Converts MAUI's flags to the native decoration bits, preserving combinations.
		/// </summary>
		public static int ResolveTextDecorations(TextDecorations decorations)
		{
			var result = NoDecorations;

			if (decorations.HasFlag(TextDecorations.Underline))
				result |= UnderlineDecoration;

			if (decorations.HasFlag(TextDecorations.Strikethrough))
				result |= StrikethroughDecoration;

			return result;
		}

		/// <summary>Native <c>Tizen.UIExtensions.Common.LineBreakMode</c> values.</summary>
		/// <remarks>
		/// Mirrored as integers so this file stays free of TizenFX, and verified against the
		/// shipped assembly's metadata.
		/// </remarks>
		public const int NoneLineBreak = 0;

		/// <summary>Native <c>NoWrap</c>.</summary>
		public const int NoWrapLineBreak = 1;

		/// <summary>Native <c>CharacterWrap</c>.</summary>
		public const int CharacterWrapLineBreak = 2;

		/// <summary>Native <c>WordWrap</c>.</summary>
		public const int WordWrapLineBreak = 3;

		/// <summary>Native <c>MixedWrap</c>.</summary>
		public const int MixedWrapLineBreak = 4;

		/// <summary>Native <c>HeadTruncation</c>.</summary>
		public const int HeadTruncationLineBreak = 5;

		/// <summary>Native <c>MiddleTruncation</c>.</summary>
		public const int MiddleTruncationLineBreak = 6;

		/// <summary>Native <c>TailTruncation</c>.</summary>
		public const int TailTruncationLineBreak = 7;

		/// <summary>
		/// Converts Controls' <c>LineBreakMode</c> ordinal to the native line-break mode.
		/// </summary>
		/// <remarks>
		/// The two enums are NOT ordinal-compatible and casting between them silently produces the
		/// wrong mode - Microsoft.Maui's NoWrap is 0 while the native NoWrap is 1, so a cast turns
		/// every NoWrap into None and shifts all six values. Both sets were read from the shipped
		/// assemblies' metadata rather than assumed; an earlier version of this table was written
		/// from a plausible guess and was wrong.
		///
		/// Anything unrecognised falls back to WordWrap, matching upstream's default arm.
		/// </remarks>
		/// <param name="lineBreakMode">The cross-platform line break mode.</param>
		public static int ResolveLineBreakMode(LineBreakMode lineBreakMode) => lineBreakMode switch
		{
			LineBreakMode.NoWrap => NoWrapLineBreak,
			LineBreakMode.WordWrap => WordWrapLineBreak,
			LineBreakMode.CharacterWrap => CharacterWrapLineBreak,
			LineBreakMode.HeadTruncation => HeadTruncationLineBreak,
			LineBreakMode.TailTruncation => TailTruncationLineBreak,
			LineBreakMode.MiddleTruncation => MiddleTruncationLineBreak,
			_ => WordWrapLineBreak,
		};

		/// <summary>
		/// The native accessibility state for a view, resolved from both Controls annotations at
		/// once.
		/// </summary>
		/// <remarks>
		/// Both must be resolved together. NUI has two flags - AccessibilityHidden and
		/// AccessibilityHighlightable - and BOTH annotations write to BOTH flags, so applying them
		/// through separate helpers means whichever mapper key ran last silently overwrites the
		/// other. An element excluded with its children would become reachable again purely because
		/// IsInAccessibleTree happened to be mapped afterwards.
		///
		/// Exclusion wins when set, because it is the stronger statement: it removes the element
		/// and its subtree regardless of what IsInAccessibleTree says.
		/// </remarks>
		/// <param name="isInAccessibleTree">AutomationProperties.IsInAccessibleTree, if set.</param>
		/// <param name="excludedWithChildren">AutomationProperties.ExcludedWithChildren, if set.</param>
		/// <returns>The hidden and highlightable flags to apply.</returns>
		public static (bool Hidden, bool Highlightable) ResolveAccessibility(
			bool? isInAccessibleTree,
			bool? excludedWithChildren)
		{
			if (excludedWithChildren == true)
				return (Hidden: true, Highlightable: false);

			if (isInAccessibleTree is bool inTree)
				return (Hidden: !inTree, Highlightable: inTree);

			// Neither annotation set: leave the element reachable, which is NUI's own default.
			return (Hidden: false, Highlightable: true);
		}

		// MaxLines is deliberately absent.
		//
		// There is no native equivalent in this TizenFX. Tizen.NUI TextLabel exposes LineCount
		// (read-only), MultiLine and Ellipsis - none of which caps the number of rendered lines -
		// and Tizen.UIExtensions.NUI.Label exposes only LineBreakMode. Read from the shipped
		// assemblies' metadata, not assumed.
		//
		// That is almost certainly why upstream MAUI marks its Tizen MapMaxLines [MissingMapper]
		// and leaves the body empty. A resolver here would be dead code dressed up as coverage.

		/// <summary>
		/// Resolves a minimum dimension to a native constraint, treating "not set" as no
		/// constraint rather than as "leave whatever was there before".
		/// </summary>
		/// <param name="value">The cross-platform minimum.</param>
		/// <param name="toScaledPixel">Converts a device-independent value to scaled pixels.</param>
		public static int ResolveMinimum(double value, Func<double, int> toScaledPixel)
		{
			ArgumentNullException.ThrowIfNull(toScaledPixel);

			// Dimension.IsExplicitSet, without taking a dependency on it: an unset minimum arrives
			// as either the unset sentinel or NaN depending on the caller.
			var isSet = !double.IsNaN(value) && value != Primitives.Dimension.Unset;

			return isSet ? toScaledPixel(value) : 0;
		}
	}
}
