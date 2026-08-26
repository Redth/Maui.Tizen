// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Behavioural coverage for the common view properties on every control handler.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These assert that a common mapper <em>ran and reached the platform view</em>, not merely
	/// that its key is registered. The defect being guarded against is subtle and was live in this
	/// PR: chaining MAUI's neutral <c>ViewHandler.ViewMapper</c> resolves every key, so a
	/// key-presence test passes, while the mapper bodies are the <c>Standard</c> no-ops compiled
	/// with <c>PlatformView</c> aliased to <see cref="object"/>. Visibility, enabled state,
	/// opacity, sizing, transforms and input transparency would all silently do nothing.
	/// </para>
	/// <para>
	/// <see cref="ControlMapperParityTests.MapperChainsFromTizenViewMapper"/> pins the chain
	/// structurally; this pins the observable effect. Both are needed - the structural test alone
	/// cannot distinguish a real mapper from a no-op with the same keys.
	/// </para>
	/// </remarks>
	public class ControlMapperBehaviorTests
	{
		/// <summary>
		/// The common properties every control must actually apply, from review item 1.
		/// </summary>
		public static IEnumerable<object[]> CommonPropertyCases() =>
			from handler in TizenControlHandlers.All
			from key in new[]
			{
				nameof(IView.Visibility),
				nameof(IView.IsEnabled),
				nameof(IView.Opacity),
				nameof(IView.Width),
				nameof(IView.Height),
				nameof(IView.MinimumWidth),
				nameof(IView.MinimumHeight),
				nameof(IView.InputTransparent),
			}
			select new object[] { handler, key };

		/// <summary>
		/// The transform properties, which all funnel through one composed NUI update.
		/// </summary>
		public static IEnumerable<object[]> TransformPropertyCases() =>
			from handler in TizenControlHandlers.All
			from key in new[]
			{
				nameof(IView.TranslationX),
				nameof(IView.TranslationY),
				nameof(IView.Scale),
				nameof(IView.ScaleX),
				nameof(IView.ScaleY),
				nameof(IView.Rotation),
				nameof(IView.RotationX),
				nameof(IView.RotationY),
				nameof(IView.AnchorX),
				nameof(IView.AnchorY),
			}
			select new object[] { handler, key };

		[Theory]
		[MemberData(nameof(CommonPropertyCases))]
		public void CommonPropertyReachesThePlatformView(TizenControlHandlers.ControlHandlerCase handler, string key)
		{
			var (element, platform) = Create(handler);
			platform.Applied.Clear();

			element.UpdateValue(key);

			Assert.True(
				platform.Applied.Contains(key),
				$"{handler.HandlerType.Name}: mapping '{key}' did not reach the platform view. " +
				"The mapper key resolves but its body is a no-op - almost always because the " +
				"handler chains MAUI's neutral ViewHandler.ViewMapper instead of " +
				"TizenViewMappers.ViewMapper.");
		}

		/// <remarks>
		/// Every transform-affecting property funnels into a single composed
		/// <c>UpdateTransformation</c> - NUI applies translation, scale and rotation as one
		/// transform about the pivot - so the recorded key is <c>"Transformation"</c> rather than
		/// the individual property name.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TransformPropertyCases))]
		public void TransformPropertyRecomputesTheTransformation(TizenControlHandlers.ControlHandlerCase handler, string key)
		{
			var (element, platform) = Create(handler);
			platform.Applied.Clear();

			element.UpdateValue(key);

			Assert.True(
				platform.Applied.Contains("Transformation"),
				$"{handler.HandlerType.Name}: transform mapping '{key}' did not recompute the " +
				"platform transformation.");
		}

		/// <summary>
		/// Handlers whose platform view is a group, and which therefore forward focus to a child.
		/// </summary>
		/// <remarks>
		/// These deliberately do not route focus through the base command mapper: the group draws
		/// no caret and accepts no input, so focusing it would resolve the request while doing
		/// nothing visible. Their forwarding is covered by
		/// <see cref="TextAndFocusProxyTests.CompositeHandlersOverrideTheFocusCommands"/> and
		/// <see cref="TextAndFocusProxyTests.CompositeViewsForwardFocusToTheirChildren"/>.
		/// </remarks>
		static readonly HashSet<Type> CompositeHandlers =
		[
			typeof(TizenSearchBarHandler),
			typeof(TizenStepperHandler),
		];

		/// <summary>
		/// Focus and unfocus must reach the platform view through the command mapper.
		/// </summary>
		/// <remarks>
		/// Commands fail differently from properties: an unmapped <c>Focus</c> means focus simply
		/// never happens, with nothing thrown and nothing logged.
		/// </remarks>
		/// <summary>Every non-composite handler, for the base focus assertion.</summary>
		public static IEnumerable<object[]> SimpleHandlerCases() =>
			TizenControlHandlers.All
				.Where(h => !CompositeHandlers.Contains(h.HandlerType))
				.Select(h => new object[] { h });

		[Theory]
		[MemberData(nameof(SimpleHandlerCases))]
		public void FocusAndUnfocusReachThePlatformView(TizenControlHandlers.ControlHandlerCase handler)
		{
			var (element, platform) = Create(handler);
			platform.Applied.Clear();

			element.Invoke(nameof(IView.Focus), new FocusRequest());
			element.Invoke(nameof(IView.Unfocus));

			Assert.True(platform.Applied.Contains(nameof(IView.Focus)), $"{handler.HandlerType.Name}: Focus did not reach the platform view.");
			Assert.True(platform.Applied.Contains(nameof(IView.Unfocus)), $"{handler.HandlerType.Name}: Unfocus did not reach the platform view.");
		}

		/// <summary>
		/// The whole common set must be applied when a handler is first connected.
		/// </summary>
		/// <remarks>
		/// Initial application goes through a different path from a later property change, so a
		/// broken chain could in principle show up on one and not the other.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void ConnectingAppliesTheCommonProperties(TizenControlHandlers.ControlHandlerCase handler)
		{
			var (_, platform) = Create(handler);

			string[] expected =
			[
				nameof(IView.Visibility),
				nameof(IView.IsEnabled),
				nameof(IView.Opacity),
				nameof(IView.InputTransparent),
			];

			var missing = expected.Where(k => !platform.Applied.Contains(k)).ToList();

			Assert.True(
				missing.Count == 0,
				$"{handler.HandlerType.Name}: connecting the handler did not apply {string.Join(", ", missing)}.");
		}

		static (IElementHandler Handler, TizenPlatformView Platform) Create(TizenControlHandlers.ControlHandlerCase handler)
		{
			var instance = (IElementHandler)Activator.CreateInstance(handler.HandlerType)!;
			instance.SetVirtualView(StubViews.For(handler.VirtualViewType));

			var platform = instance.PlatformView as TizenPlatformView
				?? throw new InvalidOperationException(
					$"{handler.HandlerType.Name} produced a platform view of type " +
					$"{instance.PlatformView?.GetType().Name ?? "null"}, which is not a TizenPlatformView.");

			return (instance, platform);
		}
	}
}
