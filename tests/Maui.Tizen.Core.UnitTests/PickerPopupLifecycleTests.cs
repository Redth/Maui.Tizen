// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

			public bool IsOpen { get; set; } = true;
		}

		sealed class FakePopup : IDisposable
		{
			readonly bool _throwOnClose;
			readonly bool _throwOnDispose;

			public FakePopup(bool throwOnClose = false, bool throwOnDispose = false)
			{
				_throwOnClose = throwOnClose;
				_throwOnDispose = throwOnDispose;
			}

			public int CloseCount { get; private set; }

			public int DisposeCount { get; private set; }

			public void Close()
			{
				CloseCount++;

				if (_throwOnClose)
					throw new InvalidOperationException("popup close");
			}

			public void Dispose()
			{
				DisposeCount++;

				if (_throwOnDispose)
					throw new InvalidOperationException("popup dispose");
			}
		}

		sealed class ScriptedDispatch
		{
			readonly Queue<DispatchBehavior> _behaviors;

			public ScriptedDispatch(params DispatchBehavior[] behaviors) =>
				_behaviors = new Queue<DispatchBehavior>(behaviors);

			public Task Invoke(Action action)
			{
				var behavior = _behaviors.Dequeue();

				if (behavior == DispatchBehavior.Throw)
					throw new InvalidOperationException("dispatch threw");

				if (behavior == DispatchBehavior.Reject)
					return Task.FromException(new InvalidOperationException("dispatch rejected"));

				action();
				return Task.CompletedTask;
			}
		}

		public enum DispatchBehavior
		{
			Run,
			Reject,
			Throw,
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
				static view => view.IsOpen,
				() => popup,
				(_, _) => completion.Task,
				static value => value.Close(),
				RunInline,
				(view, value) => applied.Add((view, value)),
				static view => view.IsOpen = false);

			Assert.True(lifecycle.IsOpen);

			completion.SetResult(7);
			await run;

			var item = Assert.Single(applied);
			Assert.Same(virtualView, item.View);
			Assert.Equal(7, item.Value);
			Assert.False(lifecycle.IsOpen);
			Assert.False(virtualView.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}

		[Fact]
		public async Task IsOpenIsTheSingleProgrammaticStateDriver()
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual") { IsOpen = false };
			var platformView = new FakeView("platform");
			var popup = new FakePopup();
			var completion = new TaskCompletionSource<int>();
			var createCount = 0;
			var applied = false;

			Task Run() => lifecycle.RunAsync(
				virtualView,
				platformView,
				() => virtualView,
				() => platformView,
				static view => view.IsOpen,
				() =>
				{
					createCount++;
					return popup;
				},
				(_, _) => completion.Task,
				static value => value.Close(),
				RunInline,
				(_, _) => applied = true,
				static view => view.IsOpen = false);

			await Run();
			Assert.Equal(0, createCount);
			Assert.False(lifecycle.IsOpen);

			virtualView.IsOpen = true;
			var active = Run();

			Assert.True(lifecycle.IsOpen);
			Assert.True(virtualView.IsOpen);
			Assert.Equal(1, createCount);

			// Repeating true cannot create a second popup.
			await Run();
			Assert.Equal(1, createCount);

			virtualView.IsOpen = false;
			await lifecycle.CancelAsync();

			Assert.False(lifecycle.IsOpen);
			Assert.False(virtualView.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);

			completion.SetResult(1);
			await active;

			Assert.False(applied);
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
				static view => view.IsOpen,
				() => oldPopup,
				(_, token) =>
				{
					oldCancellation = token;
					return oldCompletion.Task;
				},
				static value => value.Close(),
				RunInline,
				(view, value) => applied.Add((view, value)),
				static view => view.IsOpen = false);

			lifecycle.CancelOnUiThread();

			Assert.False(lifecycle.IsOpen);
			Assert.False(oldVirtualView.IsOpen);
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
				static view => view.IsOpen,
				() => newPopup,
				(_, _) => newCompletion.Task,
				static value => value.Close(),
				RunInline,
				(view, value) => applied.Add((view, value)),
				static view => view.IsOpen = false);

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
			Assert.False(oldVirtualView.IsOpen);
			Assert.False(newVirtualView.IsOpen);
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
				static view => view.IsOpen,
				() => popup,
				(_, _) => Task.FromCanceled<int>(new CancellationToken(canceled: true)),
				static value => value.Close(),
				RunInline,
				(_, _) => applied = true,
				static view => view.IsOpen = false);

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
					static view => view.IsOpen,
					() => popup,
					(_, _) => Task.FromException<int>(new InvalidOperationException("boom")),
					static value => value.Close(),
					RunInline,
					static (_, _) => { },
					static view => view.IsOpen = false));

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
				static view => view.IsOpen,
				() => popup,
				(_, token) =>
				{
					registration = token.Register(
						static () => throw new InvalidOperationException("cancel callback"));
					return completion.Task;
				},
				static value => value.Close(),
				RunInline,
				static (_, _) => { },
				static view => view.IsOpen = false);

			Assert.Throws<InvalidOperationException>(
				lifecycle.CancelOnUiThread);

			Assert.False(lifecycle.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);

			completion.SetResult(1);
			await run;
			registration.Dispose();

			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}

		[Fact]
		public async Task CancelStillClosesDisposesAndSetsClosedWhenEveryCleanupThrows()
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual");
			var platformView = new FakeView("platform");
			var popup = new FakePopup(throwOnClose: true, throwOnDispose: true);
			var completion = new TaskCompletionSource<int>();
			CancellationTokenRegistration registration = default;

			var run = lifecycle.RunAsync(
				virtualView,
				platformView,
				() => virtualView,
				() => platformView,
				static view => view.IsOpen,
				() => popup,
				(_, token) =>
				{
					registration = token.Register(
						static () => throw new InvalidOperationException("cancel callback"));
					return completion.Task;
				},
				static value => value.Close(),
				RunInline,
				static (_, _) => { },
				static view => view.IsOpen = false);

			var failure = Assert.Throws<AggregateException>(lifecycle.CancelOnUiThread);

			Assert.Equal(
				["cancel callback", "popup close", "popup dispose"],
				failure.InnerExceptions.Select(exception => exception.Message));
			Assert.False(lifecycle.IsOpen);
			Assert.False(virtualView.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);

			completion.SetResult(1);
			await run;
			registration.Dispose();

			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}

		[Theory]
		[InlineData(DispatchBehavior.Reject)]
		[InlineData(DispatchBehavior.Throw)]
		public async Task ApplyDispatchFailureDoesNotWedgeOrLeak(DispatchBehavior failure)
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual");
			var platformView = new FakeView("platform");
			var popup = new FakePopup();
			var dispatch = new ScriptedDispatch(failure, DispatchBehavior.Run);
			CancellationToken cancellation = default;

			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				lifecycle.RunAsync(
					virtualView,
					platformView,
					() => virtualView,
					() => platformView,
					static view => view.IsOpen,
					() => popup,
					(_, token) =>
					{
						cancellation = token;
						return Task.FromResult(1);
					},
					static value => value.Close(),
					dispatch.Invoke,
					static (_, _) => { },
					static view => view.IsOpen = false));

			Assert.False(lifecycle.IsOpen);
			Assert.False(virtualView.IsOpen);
			Assert.True(cancellation.IsCancellationRequested);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);

			await AssertCanOpenAgain(lifecycle, virtualView, platformView);
		}

		[Theory]
		[InlineData(DispatchBehavior.Reject)]
		[InlineData(DispatchBehavior.Throw)]
		public async Task CleanupDispatchFailureDoesNotWedgeOrDoubleDispose(DispatchBehavior failure)
		{
			var lifecycle = new TizenPopupLifecycle<FakePopup>();
			var virtualView = new FakeView("virtual");
			var platformView = new FakeView("platform");
			var popup = new FakePopup();
			var dispatch = new ScriptedDispatch(DispatchBehavior.Run, failure);
			CancellationToken cancellation = default;
			var applied = false;

			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				lifecycle.RunAsync(
					virtualView,
					platformView,
					() => virtualView,
					() => platformView,
					static view => view.IsOpen,
					() => popup,
					(_, token) =>
					{
						cancellation = token;
						return Task.FromResult(1);
					},
					static value => value.Close(),
					dispatch.Invoke,
					(_, _) => applied = true,
					static view => view.IsOpen = false));

			Assert.True(applied);
			Assert.False(lifecycle.IsOpen);
			Assert.False(virtualView.IsOpen);
			Assert.True(cancellation.IsCancellationRequested);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);

			await AssertCanOpenAgain(lifecycle, virtualView, platformView);
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
			Assert.Contains("_popupLifecycle.CancelAsync()", source, StringComparison.Ordinal);
			Assert.Contains("TizenDispatchExtensions.CaptureDispatcher", source, StringComparison.Ordinal);
			Assert.Contains("nameof(", source, StringComparison.Ordinal);
			Assert.Contains(".IsOpen)] = MapIsOpen", source, StringComparison.Ordinal);
			Assert.Contains(".IsOpen = true;", source, StringComparison.Ordinal);
			Assert.Contains(".IsOpen = false;", source, StringComparison.Ordinal);
			Assert.Contains("TizenCleanup.Run", source, StringComparison.Ordinal);
			Assert.DoesNotContain("using var popup", source, StringComparison.Ordinal);
			Assert.DoesNotContain("_isOpen", source, StringComparison.Ordinal);
			Assert.DoesNotContain("ConfigureAwait(false)", source, StringComparison.Ordinal);
		}

		static async Task AssertCanOpenAgain(
			TizenPopupLifecycle<FakePopup> lifecycle,
			FakeView virtualView,
			FakeView platformView)
		{
			virtualView.IsOpen = true;
			var popup = new FakePopup();

			await lifecycle.RunAsync(
				virtualView,
				platformView,
				() => virtualView,
				() => platformView,
				static view => view.IsOpen,
				() => popup,
				(_, _) => Task.FromResult(2),
				static value => value.Close(),
				RunInline,
				static (_, _) => { },
				static view => view.IsOpen = false);

			Assert.False(lifecycle.IsOpen);
			Assert.False(virtualView.IsOpen);
			Assert.Equal(1, popup.CloseCount);
			Assert.Equal(1, popup.DisposeCount);
		}
	}
}
