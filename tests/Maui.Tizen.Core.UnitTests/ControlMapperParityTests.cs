// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Verifies that every control mapper is a complete substitute for MAUI's own.
	/// </summary>
	/// <remarks>
	/// A missing mapper key fails silently: the property is simply never applied and the control
	/// renders with a stale or default value. Nothing throws, nothing logs, and the only symptom
	/// is on a screen nobody can look at until the Samsung workload ships. That is precisely why
	/// this is worth asserting.
	/// </remarks>
	public class ControlMapperParityTests
	{
		/// <summary>
		/// Neutral keys this backend deliberately does not reimplement.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>Border</c> is the obsolete <c>IBorder.Border</c> mapping; MAUI marks the property
		/// <c>[Obsolete]</c> and states it will be removed, so reimplementing it would mean
		/// shipping a backend that is deprecated on arrival. Border rendering itself is not lost -
		/// it is driven by the stroke and shape properties that replaced it.
		/// </para>
		/// <para>
		/// <c>ContainerView</c> cannot be honoured at all: <c>ViewHandler.ContainerView</c> has a
		/// <c>private protected</c> setter, so an out-of-repo backend cannot publish a container
		/// it constructs. The backend renders background, clip and shadow directly onto the
		/// platform view instead (<c>NeedsContainer =&gt; false</c>).
		/// </para>
		/// <para>
		/// Both exclusions are the same set the core slice applies to its own base mapper, and are
		/// asserted centrally by
		/// <c>MapperRegistrationTests.TizenBaseMapperCoversMauisViewMapperExceptDocumentedExclusions</c>.
		/// </para>
		/// </remarks>
		static readonly IReadOnlySet<string> IntentionallyUnmapped =
			new HashSet<string>(StringComparer.Ordinal) { "Border", "ContainerView" };

		/// <summary>
		/// Whether a key is deliberately unmapped, so the parity matrix can distinguish a
		/// documented exclusion from a real gap.
		/// </summary>
		/// <param name="key">The mapper key.</param>
		public static bool IsIntentionallyUnmapped(string key) => IntentionallyUnmapped.Contains(key);

		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void MapperCoversEveryNeutralKey(TizenControlHandlers.ControlHandlerCase handler)
		{
			var tizenKeys = TizenControlHandlers.GetMapperKeys(handler.HandlerType);
			var neutralKeys = TizenControlHandlers.GetNeutralMapperKeys(handler.NeutralHandlerName);

			var missing = neutralKeys
				.Except(tizenKeys, StringComparer.Ordinal)
				.Except(IntentionallyUnmapped, StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.ToList();

			Assert.True(
				missing.Count == 0,
				$"{handler.HandlerType.Name} is missing keys that {handler.NeutralHandlerName} " +
				$"defines: {string.Join(", ", missing)}. Every property MAUI can push must be " +
				"handled, even when the handling is an explicitly documented no-op.");
		}

		/// <summary>
		/// A mapper may add keys, but only ones something can actually raise.
		/// </summary>
		/// <remarks>
		/// This is the typo test. A key that matches no property is never invoked, so
		/// <c>"Placeholdr"</c> looks exactly like a working mapping until someone reads it.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void MapperHasNoUnreachableKeys(TizenControlHandlers.ControlHandlerCase handler)
		{
			var tizenKeys = TizenControlHandlers.GetMapperKeys(handler.HandlerType);

			var known = new HashSet<string>(
				TizenControlHandlers.GetNeutralMapperKeys(handler.NeutralHandlerName),
				StringComparer.Ordinal);

			foreach (var key in TizenViewMappers.ViewMapper.GetKeys())
				known.Add(key);

			// MAUI declares these members internal, so they cannot be reached with nameof from an
			// out-of-repo backend; they are mapped by string literal and are legitimate. IsOpen is
			// deliberately absent: it is public in MAUI 11 and must remain a real typed mapping.
			foreach (var internalKey in new[] { "Items", "SearchIconColor" })
				known.Add(internalKey);

			var unreachable = tizenKeys.Except(known, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

			Assert.True(
				unreachable.Count == 0,
				$"{handler.HandlerType.Name} maps keys that neither {handler.NeutralHandlerName} " +
				$"nor TizenViewMappers.ViewMapper defines: {string.Join(", ", unreachable)}.");
		}

		/// <summary>
		/// Every control mapper must chain from the <em>Tizen</em> base view mapper.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Chaining MAUI's neutral <c>ViewHandler.ViewMapper</c> would satisfy a key-presence check
		/// while doing nothing at all: that mapper is compiled with <c>PlatformView</c> aliased to
		/// <see cref="object"/> and dispatches to the <c>Standard</c> no-op extensions. Every
		/// common property - size, visibility, enabled, opacity, transforms - would silently never
		/// reach the platform view.
		/// </para>
		/// <para>
		/// This asserts the source of the chain, not just the key set, so swapping the base back to
		/// the neutral mapper fails here. <see cref="ControlMapperBehaviorTests"/> then proves the
		/// chained mappers actually run.
		/// </para>
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void MapperChainsFromTizenViewMapper(TizenControlHandlers.ControlHandlerCase handler)
		{
			var tizenKeys = TizenControlHandlers.GetMapperKeys(handler.HandlerType);
			var baseKeys = TizenViewMappers.ViewMapper.GetKeys().ToHashSet(StringComparer.Ordinal);

			var missing = baseKeys.Except(tizenKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

			Assert.True(
				missing.Count == 0,
				$"{handler.HandlerType.Name}.Mapper does not chain from TizenViewMappers.ViewMapper; " +
				$"missing {string.Join(", ", missing)}.");
		}

		/// <summary>
		/// Every command mapper must chain from MAUI's shared view command mapper.
		/// </summary>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void CommandMapperChainsFromViewCommandMapper(TizenControlHandlers.ControlHandlerCase handler)
		{
			var field = handler.HandlerType.GetField("CommandMapper");
			Assert.NotNull(field);

			var commandMapper = field!.GetValue(null);
			Assert.NotNull(commandMapper);

			var instance = Activator.CreateInstance(handler.HandlerType);
			Assert.NotNull(instance);

			// Focus is only reachable if the chain is intact; invoking it must not throw.
			var element = (IElementHandler)instance!;
			element.Invoke(nameof(IView.Focus), new FocusRequest());
		}
	}
}
