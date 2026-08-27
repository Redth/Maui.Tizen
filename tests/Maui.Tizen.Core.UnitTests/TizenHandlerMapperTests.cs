// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Verifies how each handler's mappers are composed on top of MAUI's.
	/// </summary>
	/// <remarks>
	/// The backend is reachable from MAUI Controls only if each handler chains MAUI's static
	/// mapper and implements MAUI's handler interface. These pin both, plus the layering that
	/// keeps the chained no-op bodies from winning.
	/// </remarks>
	public class TizenHandlerMapperTests
	{
		/// <summary>
		/// Each handler implements MAUI's real handler interface.
		/// </summary>
		/// <remarks>
		/// This is what makes Controls' <c>RemapForControls</c> dispatch land here: MAUI's
		/// mappings hard-cast the handler to the interface they were declared against, so a
		/// backend-only interface would throw <see cref="InvalidCastException"/> the moment a
		/// chained mapping ran.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void HandlerImplementsMauisHandlerInterface(TizenControlHandlers.ControlHandlerCase handler)
		{
			var mauiInterface = typeof(IView).Assembly.GetType($"Microsoft.Maui.Handlers.I{handler.NeutralHandlerName}");

			Assert.True(mauiInterface is not null, $"Microsoft.Maui.Handlers.I{handler.NeutralHandlerName} was not found.");

			Assert.True(
				mauiInterface!.IsAssignableFrom(handler.HandlerType),
				$"{handler.HandlerType.Name} does not implement {mauiInterface.Name}. Controls' " +
				"remapped mappings hard-cast to that interface, so they would throw at runtime.");
		}

		/// <summary>
		/// Every chained mapping runs against the real handler without an invalid cast.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The direct test of the hard-cast concern: each key MAUI or Controls contributed is
		/// invoked against a live handler. An <see cref="InvalidCastException"/> here means the
		/// backend is not actually substitutable for MAUI's own handler.
		/// </para>
		/// <para>
		/// The underlying invariant is stronger than it looks, so it is worth stating precisely.
		/// MAUI's static mappers are *declared* as <c>IPropertyMapper&lt;IView, IXHandler&gt;</c> but
		/// *constructed* as <c>PropertyMapper&lt;IView, XHandler&gt;</c>, closed over the concrete
		/// handler; <c>PropertyMapper&lt;,&gt;.Add</c> then dispatches through a hard
		/// <c>(TViewHandler)h</c> cast. Chaining alone therefore does not make a chained key usable -
		/// every key contributed by a chained MAUI/Controls mapper must be *overridden* by this
		/// backend, or dispatching it to a Tizen handler throws. This test is the guard for that, and
		/// it is also a drift alarm: a future MAUI package that adds a key will fail here rather than
		/// on device.
		/// </para>
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void EveryChainedMappingInvokesWithoutCastFailure(TizenControlHandlers.ControlHandlerCase handler)
		{
			ControlsRemap.Force();

			var instance = (IElementHandler)Activator.CreateInstance(handler.HandlerType)!;
			instance.SetVirtualView(StubViews.For(handler.VirtualViewType));

			var failures = new List<string>();

			foreach (var key in TizenControlHandlers.GetMapperKeys(handler.HandlerType))
			{
				try
				{
					instance.UpdateValue(key);
				}
				catch (InvalidCastException ex)
				{
					failures.Add($"{key}: {ex.Message}");
				}
				catch (Exception)
				{
					// Other failures are off-platform side effects of a no-op stand-in; only a
					// cast failure indicates the composition itself is wrong.
				}
			}

			Assert.True(
				failures.Count == 0,
				$"{handler.HandlerType.Name} could not dispatch chained mappings:\n  " +
				string.Join("\n  ", failures) +
				"\n\nMAUI's static mappers are constructed as PropertyMapper<TVirtualView, ConcreteHandler>, " +
				"so PropertyMapper<,>.Add dispatches through a hard (ConcreteHandler)h cast. Chaining a " +
				"mapper is not enough: this backend must OVERRIDE every key the chained mapper " +
				"contributes. Add the key above to the handler's own mapper, mirroring what MAUI or " +
				"Controls does for it.");
		}

		/// <summary>
		/// Handler mappers chain MAUI's static mapper, so Controls remaps reach the backend.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Asserted by observing a key that only exists once Controls has remapped, rather than
		/// by inspecting the chain structure - it is the reachability that matters.
		/// </para>
		/// <para>
		/// <b>Reachability only. This says nothing about whether a key does anything.</b> By this
		/// suite's own standard - resolution is not implementation - a passing case here means the
		/// key arrives at the backend, not that the backend acts on it. In particular
		/// <c>TizenLabelHandler</c>'s <c>FormattedText</c> and <c>MaxLines</c> are reachable and
		/// <b>not implemented</b>: Core binds <c>MapMaxLines</c> as an empty <c>[MissingMapper]</c>
		/// and does not bind <c>FormattedText</c> at all. Both are assigned to Wave A and are
		/// specified in <c>docs/wave-a-integration-plan.md</c> §5. Do not read this test as parity.
		/// </para>
		/// </remarks>
		[Theory]
		[InlineData(typeof(TizenCheckBoxHandler), "Color")]
		[InlineData(typeof(TizenButtonHandler), "IsInAccessibleTree")]
		[InlineData(typeof(TizenEntryHandler), "Description")]
		[InlineData(typeof(TizenPickerHandler), "Hint")]
		[InlineData(typeof(TizenPickerHandler), "ItemsSource")]
		[InlineData(typeof(TizenStepperHandler), "Increment")]
		[InlineData(typeof(TizenLabelHandler), "FormattedText")]
		[InlineData(typeof(TizenLabelHandler), "TextType")]
		[InlineData(typeof(TizenLabelHandler), "MaxLines")]
		public void ControlsRemappedKeysReachTheBackend(Type handlerType, string key)
		{
			ControlsRemap.Force();

			var keys = TizenControlHandlers.GetMapperKeys(handlerType);

			Assert.True(
				keys.Contains(key),
				$"{handlerType.Name} does not expose '{key}', which MAUI Controls adds via " +
				"RemapForControls. The handler is not chaining MAUI's static mapper, so an " +
				"application using Controls would never see that property applied.");
		}

		/// <summary>
		/// The Tizen view mappings win over the chained MAUI ones.
		/// </summary>
		/// <remarks>
		/// Chaining MAUI's mapper also inherits its bodies, which are the off-platform no-ops.
		/// Without the second layer every common view property would resolve and do nothing -
		/// invisible to a key-presence test, which is exactly how it shipped the first time.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void TizenViewMappingsShadowTheChainedNoOps(TizenControlHandlers.ControlHandlerCase handler)
		{
			var instance = (IElementHandler)Activator.CreateInstance(handler.HandlerType)!;
			instance.SetVirtualView(StubViews.For(handler.VirtualViewType));

			var platform = (TizenPlatformView)instance.PlatformView!;
			platform.Applied.Clear();

			instance.UpdateValue(nameof(IView.Visibility));

			Assert.True(
				platform.Applied.Contains(nameof(IView.Visibility)),
				$"{handler.HandlerType.Name}: the chained MAUI no-op won over the Tizen body. " +
				"TizenHandlerMappers.Chain must layer the Tizen view mappings over MAUI's.");
		}

		/// <summary>
		/// A handler's own key still wins over the Tizen view mapping.
		/// </summary>
		/// <remarks>
		/// Ordering is load-bearing: <c>Entry.Background</c> re-evaluates the container before
		/// painting, so the handler's override must sit above the generic implementation.
		/// </remarks>
		[Fact]
		public void HandlerSpecificKeysWinOverTheTizenViewMappings()
		{
			var handlerKeys = TizenControlHandlers.GetMapperKeys(typeof(TizenEntryHandler));

			Assert.Contains(nameof(IView.Background), handlerKeys);

			// DeclaredOnly matters: ViewHandler also has a static MapBackground, and an
			// unqualified lookup is ambiguous between the two.
			var declared = typeof(TizenEntryHandler)
				.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
				.FirstOrDefault(m => m.Name == "MapBackground");

			Assert.True(
				declared is not null,
				"TizenEntryHandler must declare its own MapBackground; inheriting the generic one " +
				"would paint before the container had been re-evaluated.");
		}

		/// <summary>
		/// The literal view-command list matches what the Tizen base mapper resolves.
		/// </summary>
		/// <remarks>
		/// <see cref="CommandMapper"/> exposes no key enumeration, so
		/// <see cref="TizenHandlerMappers.ViewCommandKeys"/> is written out by hand. This keeps
		/// that copy honest.
		/// </remarks>
		[Fact]
		public void ViewCommandKeysMatchTheTizenBaseMapper()
		{
			foreach (var key in TizenHandlerMappers.ViewCommandKeys)
			{
				Assert.True(
					TizenViewMappers.ViewCommandMapper.GetCommand(key) is not null,
					$"TizenHandlerMappers.ViewCommandKeys lists '{key}', which the Tizen base " +
					"command mapper does not implement.");
			}

			foreach (var key in new[] { nameof(IView.Focus), nameof(IView.Unfocus), nameof(IView.InvalidateMeasure), nameof(IView.Frame) })
			{
				Assert.True(
					TizenHandlerMappers.ViewCommandKeys.Contains(key),
					$"The Tizen base command mapper implements '{key}' but " +
					"TizenHandlerMappers.ViewCommandKeys omits it, so it would never be layered " +
					"over MAUI's no-op.");
			}
		}

		/// <summary>
		/// The Controls-remapped keys and the focus commands actually <em>execute</em> against a
		/// Tizen handler, not merely resolve.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Key presence and command presence are cheap to satisfy and prove little - a chained
		/// mapper makes every key resolve. This dispatches through the real composed mappers on a
		/// real handler bound to a real Controls virtual view, so a mapping that resolves to
		/// something uncallable fails here.
		/// </para>
		/// <para>
		/// <see cref="InvalidCastException"/> is the failure that matters: MAUI's static mappers are
		/// closed over its concrete handler type, so a key this backend has not overridden throws
		/// rather than no-ops. Off-platform no-op side effects are ignored, since the host lane has
		/// no NUI.
		/// </para>
		/// </remarks>
		[Fact]
		public void ControlsRemappedPropertiesAndFocusCommandsExecute()
		{
			ControlsRemap.Force();

			var label = new Controls.Label();
			var labelHandler = new TizenLabelHandler();
			labelHandler.SetVirtualView(label);

			// FormattedText is contributed exclusively by Label.RemapForControls.
			DispatchProperty(labelHandler, label, "FormattedText");
			DispatchProperty(labelHandler, label, "TextType");

			var entry = new Controls.Entry();
			var entryHandler = new TizenEntryHandler();
			entryHandler.SetVirtualView(entry);

			foreach (var command in new[] { nameof(IView.Focus), nameof(IView.Unfocus) })
				DispatchCommand(entryHandler, entry, command, new FocusRequest());

			// The two keys that were genuinely throwing before they were given Tizen bodies.
			var picker = new Controls.Picker();
			var pickerHandler = new TizenPickerHandler();
			pickerHandler.SetVirtualView(picker);
			DispatchProperty(pickerHandler, picker, "ItemsSource");

			var stepper = new Controls.Stepper();
			var stepperHandler = new TizenStepperHandler();
			stepperHandler.SetVirtualView(stepper);
			DispatchProperty(stepperHandler, stepper, "Increment");
		}

		static void DispatchProperty(IElementHandler handler, IElement view, string key)
		{
			// Without this the test would be vacuous: UpdateValue on a key no mapper defines does
			// nothing at all and passes silently.
			Assert.True(
				TizenControlHandlers.GetMapperKeys(handler.GetType()).Contains(key),
				$"{handler.GetType().Name}.Mapper does not define '{key}', so dispatching it is a " +
				"no-op and this test would prove nothing.");

			try
			{
				handler.UpdateValue(key);
			}
			catch (InvalidCastException ex)
			{
				Assert.Fail(
					$"Dispatching '{key}' to {handler.GetType().Name} threw InvalidCastException: " +
					$"{ex.Message}. The key resolves through MAUI's chained mapper, which is closed " +
					"over MAUI's concrete handler, so this backend must override it.");
			}
			catch (Exception)
			{
				// An off-platform no-op stand-in has no NUI to touch; only a cast failure means the
				// composition itself is wrong.
			}
		}

		static void DispatchCommand(IElementHandler handler, IElement view, string key, object? args)
		{
			Assert.True(
				TizenControlHandlers.GetCommandMapperCommand(handler.GetType(), key) is not null,
				$"{handler.GetType().Name}.CommandMapper does not resolve '{key}', so invoking it " +
				"is a no-op and this test would prove nothing.");

			try
			{
				handler.Invoke(key, args);
			}
			catch (InvalidCastException ex)
			{
				Assert.Fail(
					$"Invoking command '{key}' on {handler.GetType().Name} threw " +
					$"InvalidCastException: {ex.Message}.");
			}
			catch (Exception)
			{
			}
		}

		/// <summary>
		/// A mapping referenced by a mapper must not be declared inside <c>#if TIZEN</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The other half of the silent-rebinding trap. If <c>MapBackground</c> is declared inside
		/// a <c>#if TIZEN</c> block but the mapper initializer sits outside it, then on the host
		/// lane the name resolves to MAUI's <em>inherited</em> <c>ViewHandler.MapBackground</c> -
		/// the off-platform no-op. It compiles, and the handler silently behaves differently on
		/// the two target frameworks.
		/// </para>
		/// <para>
		/// The rule is therefore: declare the mapping unconditionally, guard only its body.
		/// </para>
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void MappingsReferencedByAMapperAreDeclaredUnconditionally(TizenControlHandlers.ControlHandlerCase handler)
		{
			var path = System.IO.Path.Combine(
				TestRepositoryPaths.Root, "src", "Maui.Tizen.Core", "Handlers", handler.HandlerType.Name + ".cs");

			var lines = System.IO.File.ReadAllLines(path);

			// Names used as mapper values, e.g. `[nameof(IEntry.Background)] = MapBackground,`.
			var referenced = lines
				.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"\]\s*=\s*(Map\w+)\s*,"))
				.Where(m => m.Success)
				.Select(m => m.Groups[1].Value)
				.ToHashSet(StringComparer.Ordinal);

			var offenders = new List<string>();
			var depth = 0;

			foreach (var line in lines)
			{
				var text = line.Trim();

				if (text.StartsWith("#if", StringComparison.Ordinal))
					depth++;
				else if (text.StartsWith("#endif", StringComparison.Ordinal))
					depth--;

				var declaration = System.Text.RegularExpressions.Regex.Match(
					text, @"^public static (?:void|Task|async Task) (Map\w+)\(");

				if (declaration.Success && depth > 0 && referenced.Contains(declaration.Groups[1].Value))
					offenders.Add(declaration.Groups[1].Value);
			}

			Assert.True(
				offenders.Count == 0,
				$"{handler.HandlerType.Name} declares {string.Join(", ", offenders)} inside a " +
				"conditional block while referencing it from a mapper. On a target framework where " +
				"the block is excluded, the key silently binds to MAUI's inherited no-op instead. " +
				"Declare the mapping unconditionally and guard only its body.");
		}

		/// <summary>
		/// No mapping may take the concrete handler type.
		/// </summary>
		/// <remarks>
		/// This is the trap that has now bitten twice. A mapping declared as
		/// <c>Map(TizenXHandler, ...)</c> cannot satisfy <c>Action&lt;IXHandler, ...&gt;</c>, so
		/// the name silently binds to MAUI's <em>inherited</em> <c>ViewHandler.MapFocus</c>
		/// instead - which compiles, and does nothing. Requiring the interface in the signature
		/// makes the mistake impossible rather than merely detectable.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void MappingsTakeMauisHandlerInterfaceNotTheConcreteType(TizenControlHandlers.ControlHandlerCase handler)
		{
			var offenders = handler.HandlerType
				.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
				.Where(m => m.Name.StartsWith("Map", StringComparison.Ordinal))
				.Where(m => m.GetParameters().FirstOrDefault()?.ParameterType == handler.HandlerType)
				.Select(m => m.Name)
				.ToList();

			Assert.True(
				offenders.Count == 0,
				$"{handler.HandlerType.Name} declares {string.Join(", ", offenders)} taking the " +
				"concrete handler type. Such a method cannot bind to the mapper's delegate, so " +
				"the key silently resolves to MAUI's inherited no-op instead.");
		}
	}
}
