// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	[Collection(StaticMapperCollection.Name)]
	public class SwipeStructureCoordinatorTests
	{
		[Fact]
		public void InvalidatingAnOpenValidSideResetsStateAndRequestsOnlyThatSide()
		{
			var isOpen = true;
			SwipeDirection? direction = SwipeDirection.Left;
			var offset = 42d;
			var threshold = 20d;

			var rebuild = TizenSwipeStructureCoordinator.Invalidate(
				wasOpen: true,
				previousDirection: direction,
				ref isOpen,
				ref direction,
				ref offset,
				ref threshold,
				candidate => candidate == SwipeDirection.Left);

			Assert.Equal(SwipeDirection.Left, rebuild);
			Assert.False(isOpen);
			Assert.Null(direction);
			Assert.Equal(0, offset);
			Assert.Equal(0, threshold);
		}

		[Fact]
		public void RemovedOrInvalidSideIsNotRebuilt()
		{
			var isOpen = true;
			SwipeDirection? direction = SwipeDirection.Right;
			var offset = 12d;
			var threshold = 9d;

			var rebuild = TizenSwipeStructureCoordinator.Invalidate(
				wasOpen: true,
				previousDirection: direction,
				ref isOpen,
				ref direction,
				ref offset,
				ref threshold,
				static _ => false);

			Assert.Null(rebuild);
		}

		[Fact]
		public void EveryItemCollectionMapperInvalidatesTheProductionPlatformView()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var handler = Assert.IsType<TizenSwipeViewHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(SwipeView)));
			Assert.IsAssignableFrom<ISwipeViewHandler>(handler);
			var elementHandler = (IElementHandler)handler;
			elementHandler.SetMauiContext(new MauiContext(app.Services));
			var view = new SwipeView();
			elementHandler.SetVirtualView(view);
			var platform = Assert.IsType<TizenSwipeViewGroup>(elementHandler.PlatformView);
			var before = platform.StructuralInvalidationCount;

			view.LeftItems = [new SwipeItem()];
			view.TopItems = [new SwipeItem()];
			view.RightItems = [new SwipeItem()];
			view.BottomItems = [new SwipeItem()];

			Assert.Equal(before + 4, platform.StructuralInvalidationCount);

			elementHandler.UpdateValue(nameof(ISwipeView.LeftItems));
			Assert.Equal(before + 4, platform.StructuralInvalidationCount);

			elementHandler.DisconnectHandler();
		}

		[Fact]
		public void DrainingActionItemsRejectsLateTouchesAndLeavesOnlyRebuiltItems()
		{
			var registry = new TizenSwipeItemRegistry<object, object>();
			var itemA = new object();
			var itemB = new object();
			var viewA = new object();
			var viewB = new object();
			registry.Add(itemA, viewA);
			registry.Add(itemB, viewB);
			var oldGeneration = registry.CurrentGeneration;

			var removed = registry.Drain();

			Assert.Equal(2, removed.Count);
			Assert.False(registry.IsCurrent(oldGeneration, itemA, viewA));
			Assert.False(registry.TryGetValue(itemB, out _));

			var itemC = new object();
			var viewC = new object();
			registry.Add(itemC, viewC);

			Assert.True(registry.IsCurrent(registry.CurrentGeneration, itemC, viewC));
			Assert.Single(registry.Drain());
			Assert.False(registry.TryGetValue(itemC, out _));
		}

		sealed class ControlsApp : Application
		{
		}
	}
}
