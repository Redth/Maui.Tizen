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
			var restored = 0;

			var rebuild = TizenSwipeStructureCoordinator.Invalidate(
				wasOpen: true,
				previousDirection: direction,
				ref isOpen,
				ref direction,
				ref offset,
				ref threshold,
				() => restored++,
				candidate => candidate == SwipeDirection.Left);

			Assert.Equal(SwipeDirection.Left, rebuild);
			Assert.False(isOpen);
			Assert.Null(direction);
			Assert.Equal(0, offset);
			Assert.Equal(0, threshold);
			Assert.Equal(1, restored);
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
				static () => { },
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

		[Fact]
		public void ReentrantItemMaterializationCannotCommitIntoReplacedActionTree()
		{
			var registry = new TizenSwipeItemRegistry<object, object>();
			var staleItem = new object();
			var staleView = new object();
			var currentItem = new object();
			var currentView = new object();
			var staleDisposed = 0;

			var staleOperation = registry.ReserveMaterialization();

			// External materialization changes the collection/action tree before stale commit.
			registry.Drain();
			var currentOperation = registry.ReserveMaterialization();
			Assert.True(registry.CommitPrepared(
				currentOperation,
				currentItem,
				currentView,
				handler: null,
				owner: new object(),
				static () => true,
				static () => throw new Xunit.Sdk.XunitException("Current item was disposed.")));

			Assert.False(registry.CommitPrepared(
				staleOperation,
				staleItem,
				staleView,
				handler: null,
				owner: new object(),
				static () => true,
				() => staleDisposed++));

			Assert.Equal(1, staleDisposed);
			Assert.False(registry.TryGetValue(staleItem, out _));
			Assert.True(registry.TryGetValue(currentItem, out var retained));
			Assert.Same(currentView, retained);
		}

		[Fact]
		public void ReentrantMaterializationAdoptsSamePairExactlyOnce()
		{
			var registry = new TizenSwipeItemRegistry<object, object>();
			var item = new object();
			var view = new object();
			var handler = new object();
			var outerOwner = new object();
			var currentOwner = new object();
			var outerOperation = registry.ReserveMaterialization();
			var adopted = 0;
			var disposed = 0;
			var reentered = false;

			registry.MaterializeFrozen(
				outerOperation,
				new[] { item },
				outerOwner,
				currentItem =>
				{
					if (!reentered)
					{
						reentered = true;
						registry.Drain();
						var currentOperation = registry.ReserveMaterialization();
						registry.MaterializeFrozen(
							currentOperation,
							new[] { currentItem },
							currentOwner,
							_ => view,
							_ => handler,
							static () => true,
							(_, _) => adopted++,
							(_, _, _) => disposed++);
					}

					return view;
				},
				_ => handler,
				static () => true,
				(_, _) => adopted++,
				(_, _, _) => disposed++);

			Assert.Equal(1, adopted);
			Assert.Equal(0, disposed);
			Assert.True(registry.Owns(item, view, handler));
			Assert.Single(registry.Drain());
		}

		[Fact]
		public void MaterializationEnumeratesTheFrozenSnapshot()
		{
			var registry = new TizenSwipeItemRegistry<ISwipeItem, object>();
			var first = new SwipeItem();
			var second = new SwipeItem();
			var liveItems = new SwipeItems { first, second };
			var swipeView = new SwipeView { LeftItems = liveItems };
			var snapshot = TizenSwipeItemsSnapshot.Capture(((ISwipeView)swipeView).LeftItems);
			var owner = new object();
			var operation = registry.ReserveMaterialization();
			var materialized = new List<ISwipeItem>();

			registry.MaterializeFrozen(
				operation,
				snapshot.Items,
				owner,
				item =>
				{
					materialized.Add(item);
					if (ReferenceEquals(item, first))
						liveItems.Clear();
					return new object();
				},
				static _ => null,
				static () => true,
				static (_, _) => { },
				static (_, _, _) => { });

			Assert.Equal(new ISwipeItem[] { first, second }, materialized);
		}

		[Fact]
		public void SameSideOpenDuringAnimatedCloseQueuesAndReplays()
		{
			var coordinator = new TizenSwipeOpenCoordinator();
			Assert.True(coordinator.BeginAnimatedClose());
			Assert.False(coordinator.BeginAnimatedClose());

			var decision = coordinator.RequestOpen(
				isOpen: true,
				previous: OpenSwipeItem.LeftItems,
				requested: OpenSwipeItem.LeftItems,
				animated: true);

			Assert.Equal(TizenSwipeOpenDecision.Queued, decision);

			var queued = coordinator.CompleteClose();
			Assert.NotNull(queued);
			Assert.Equal(OpenSwipeItem.LeftItems, queued.Value.Item);
			Assert.True(queued.Value.Animated);

			Assert.Equal(
				TizenSwipeOpenDecision.Open,
				coordinator.RequestOpen(
					isOpen: false,
					previous: OpenSwipeItem.LeftItems,
					requested: queued.Value.Item,
					animated: queued.Value.Animated));
		}

		[Fact]
		public void VisibleNativeChildrenPairWithFilteredVisibleVirtualItems()
		{
			var items = new[]
			{
				(Name: "hidden", Visible: false),
				(Name: "first-visible", Visible: true),
				(Name: "second-visible", Visible: true),
			};
			var views = new[]
			{
				(Name: "native-hidden", Visible: false),
				(Name: "native-first", Visible: true),
				(Name: "native-second", Visible: true),
			};

			var pairs = TizenSwipeStructureCoordinator.PairVisible(
				items,
				item => item.Visible,
				views,
				view => view.Visible);

			Assert.Collection(
				pairs,
				pair =>
				{
					Assert.Equal("first-visible", pair.Item.Name);
					Assert.Equal("native-first", pair.View.Name);
				},
				pair =>
				{
					Assert.Equal("second-visible", pair.Item.Name);
					Assert.Equal("native-second", pair.View.Name);
				});
		}

		[Fact]
		public void DisablingActiveGestureClearsTerminalStateBeforeReenable()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var view = new SwipeView { IsEnabled = true };
			var handler = Assert.IsType<TizenSwipeViewHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(SwipeView)));
			var elementHandler = (IElementHandler)handler;
			elementHandler.SetMauiContext(new MauiContext(app.Services));
			elementHandler.SetVirtualView(view);
			var platform = Assert.IsType<TizenSwipeViewGroup>(elementHandler.PlatformView);
			platform.BeginGestureForTest();

			view.IsEnabled = false;
			elementHandler.UpdateValue(nameof(IView.IsEnabled));

			Assert.False(platform.GestureActive);
			Assert.Equal(0, platform.GestureOffset);

			view.IsEnabled = true;
			elementHandler.UpdateValue(nameof(IView.IsEnabled));
			platform.BeginGestureForTest();
			Assert.True(platform.GestureActive);

			elementHandler.DisconnectHandler();
		}

		sealed class ControlsApp : Application
		{
		}
	}
}
