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
		/// Neutral keys Wave A deliberately does not reimplement.
		/// </summary>
		/// <remarks>
		/// <c>Border</c> is the obsolete <c>IBorder.Border</c> mapping; MAUI marks the property
		/// <c>[Obsolete]</c> and states it will be removed, so reimplementing it would mean
		/// shipping a backend that is deprecated on arrival. Border rendering itself is not lost -
		/// it is driven by the stroke and shape properties that replaced it.
		/// </remarks>
		static readonly IReadOnlySet<string> IntentionallyUnmapped =
			new HashSet<string>(StringComparer.Ordinal) { "Border" };

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

			foreach (var key in TizenControlHandlers.GetMapperKeys(typeof(ViewHandler), nameof(ViewHandler.ViewMapper)))
				known.Add(key);

			// MAUI declares these members internal, so they cannot be reached with nameof from an
			// out-of-repo backend; they are mapped by string literal and are legitimate.
			foreach (var internalKey in new[] { "IsOpen", "Items", "SearchIconColor" })
				known.Add(internalKey);

			var unreachable = tizenKeys.Except(known, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

			Assert.True(
				unreachable.Count == 0,
				$"{handler.HandlerType.Name} maps keys that neither {handler.NeutralHandlerName} " +
				$"nor ViewHandler.ViewMapper defines: {string.Join(", ", unreachable)}.");
		}

		/// <summary>
		/// Every control mapper must chain from MAUI's shared view mapper.
		/// </summary>
		/// <remarks>
		/// Without the chain a control maps its own properties and none of the common ones - no
		/// background, no opacity, no visibility - which reads as a rendering bug rather than a
		/// registration one.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void MapperChainsFromViewMapper(TizenControlHandlers.ControlHandlerCase handler)
		{
			var tizenKeys = TizenControlHandlers.GetMapperKeys(handler.HandlerType);
			var viewKeys = TizenControlHandlers.GetMapperKeys(typeof(ViewHandler), nameof(ViewHandler.ViewMapper));

			var missing = viewKeys.Except(tizenKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

			Assert.True(
				missing.Count == 0,
				$"{handler.HandlerType.Name}.Mapper does not chain from ViewHandler.ViewMapper; " +
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
