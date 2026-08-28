// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Executes the popup ownership state machine without requiring Tizen.NUI.
	/// </summary>
	public class PickerPopupLifecycleTests
	{
		sealed class FakeView
		{
			public FakeView(string name) => Name = name;

			public string Name { get; }
		}

		sealed class FakePopup : IDisposable
		{
			public int CloseCount { get; private set; }

			public int DisposeCount { get; private set; }

			public void Close() => CloseCount++;

			public void Dispose() => DisposeCount++;
		}

		static Task RunInline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		[Fact]
		public async Task CompletionAppliesToOriginatingViewsThenClosesExactlyOnce()
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual");
			var platformView = new FakeView("platform");
			FakeView? currentVirtualView = virtualView;
			FakeView? currentPlatformView = platformView;
			var popup = new FakePopup();
			var completion = new TaskCompletionSource<int>();
			var applied = new List<(FakeView View, int Value)>();

			var run = lifecycle.RunAsync(
				virtualView,
				platformView,
				() => currentVirtualView,
				() => currentPlatformView,
				() => popup,
				(_, _) => completion.Task,
				static value => value.Close(),
				RunInline,
				(view, value) => applied.Add((view, value)));

			Assert.True(lifecycle.IsOpen);

			completion.SetResult(7);
			await run;

			var item = Assert.Single(applied);
			Assert.Same(virtualView, item.View);
			Assert.Equal(7, item.Value);
			Assert.False(lifecycle.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}

		[Fact]
		public async Task CancelledOldPopupCannotWriteIntoReplacementViews()
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var oldVirtualView = new FakeView("old virtual");
			var oldPlatformView = new FakeView("old platform");
			var newVirtualView = new FakeView("new virtual");
			var newPlatformView = new FakeView("new platform");
			FakeView? currentVirtualView = oldVirtualView;
			FakeView? currentPlatformView = oldPlatformView;
			var oldPopup = new FakePopup();
			var newPopup = new FakePopup();
			var oldCompletion = new TaskCompletionSource<int>();
			var newCompletion = new TaskCompletionSource<int>();
			var applied = new List<(FakeView View, int Value)>();
			CancellationToken oldCancellation = default;

			var oldRun = lifecycle.RunAsync(
				oldVirtualView,
				oldPlatformView,
				() => currentVirtualView,
				() => currentPlatformView,
				() => oldPopup,
				(_, token) =>
				{
					oldCancellation = token;
					return oldCompletion.Task;
				},
				static value => value.Close(),
				RunInline,
				(view, value) => applied.Add((view, value)));

			lifecycle.CancelOnUiThread(static value => value.Close());

			Assert.False(lifecycle.IsOpen);
			Assert.True(oldCancellation.IsCancellationRequested);
			Assert.Equal(1, oldPopup.CloseCount);
			Assert.Equal(1, oldPopup.DisposeCount);

			currentVirtualView = newVirtualView;
			currentPlatformView = newPlatformView;

			var newRun = lifecycle.RunAsync(
				newVirtualView,
				newPlatformView,
				() => currentVirtualView,
				() => currentPlatformView,
				() => newPopup,
				(_, _) => newCompletion.Task,
				static value => value.Close(),
				RunInline,
				(view, value) => applied.Add((view, value)));

			newCompletion.SetResult(2);
			await newRun;

			// The already-disposed old popup completes last. It must not target the new view or be
			// disposed a second time.
			oldCompletion.SetResult(1);
			await oldRun;

			var item = Assert.Single(applied);
			Assert.Same(newVirtualView, item.View);
			Assert.Equal(2, item.Value);
			Assert.Equal(1, oldPopup.CloseCount);
			Assert.Equal(1, oldPopup.DisposeCount);
			Assert.Equal(1, newPopup.CloseCount);
			Assert.Equal(1, newPopup.DisposeCount);
		}

		[Fact]
		public async Task UserCancellationClosesAndDisposesWithoutApplying()
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual");
			var platformView = new FakeView("platform");
			var popup = new FakePopup();
			var applied = false;

			await lifecycle.RunAsync(
				virtualView,
				platformView,
				() => virtualView,
				() => platformView,
				() => popup,
				(_, _) => Task.FromCanceled<int>(new CancellationToken(canceled: true)),
				static value => value.Close(),
				RunInline,
				(_, _) => applied = true);

			Assert.False(applied);
			Assert.False(lifecycle.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}

		[Fact]
		public async Task PopupExceptionStillClosesAndDisposesExactlyOnce()
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual");
			var platformView = new FakeView("platform");
			var popup = new FakePopup();

			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				lifecycle.RunAsync(
					virtualView,
					platformView,
					() => virtualView,
					() => platformView,
					() => popup,
					(_, _) => Task.FromException<int>(new InvalidOperationException("boom")),
					static value => value.Close(),
					RunInline,
					static (_, _) => { }));

			Assert.False(lifecycle.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}

		[Fact]
		public async Task ThrowingCancellationCallbackCannotLeakThePopup()
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual");
			var platformView = new FakeView("platform");
			var popup = new FakePopup();
			var completion = new TaskCompletionSource<int>();
			CancellationTokenRegistration registration = default;

			var run = lifecycle.RunAsync(
				virtualView,
				platformView,
				() => virtualView,
				() => platformView,
				() => popup,
				(_, token) =>
				{
					registration = token.Register(
						static () => throw new InvalidOperationException("cancel callback"));
					return completion.Task;
				},
				static value => value.Close(),
				RunInline,
				static (_, _) => { });

			Assert.Throws<AggregateException>(
				() => lifecycle.CancelOnUiThread(static value => value.Close()));

			Assert.False(lifecycle.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);

			completion.SetResult(1);
			await run;
			registration.Dispose();

			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}

		[Theory]
		[InlineData("TizenPickerHandler.cs")]
		[InlineData("TizenDatePickerHandler.cs")]
		[InlineData("TizenTimePickerHandler.cs")]
		public void PickerHandlersUseOwnedAwaitablePopupLifecycle(string fileName)
		{
			var source = File.ReadAllText(Path.Combine(
				TestRepositoryPaths.Root,
				"src",
				"Maui.Tizen.Core",
				"Handlers",
				fileName));

			Assert.Contains("TizenPopupLifecycle<", source, StringComparison.Ordinal);
			Assert.Contains("_popupLifecycle.CancelOnUiThread", source, StringComparison.Ordinal);
			Assert.Contains("this.DispatchIfRequiredAsync", source, StringComparison.Ordinal);
			Assert.Contains("var virtualView = VirtualView;", source, StringComparison.Ordinal);
			Assert.Contains("var platformView = PlatformView;", source, StringComparison.Ordinal);
			Assert.DoesNotContain("using var popup", source, StringComparison.Ordinal);
			Assert.DoesNotContain("_isOpen", source, StringComparison.Ordinal);
			Assert.DoesNotContain("ConfigureAwait(false)", source, StringComparison.Ordinal);
		}
	}
}
