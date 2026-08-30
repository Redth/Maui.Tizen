using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// An in-memory navigation stack that records the operations performed on it.
/// </summary>
internal sealed class FakeNavigationStack : ITizenNavigationStack
{
	readonly List<object> _entries = new();
	bool _shownBehindPage;

	public List<string> Operations { get; } = new();

	public List<bool> PushAnimations { get; } = new();

	public List<bool> PopAnimations { get; } = new();

	public IReadOnlyList<object> Entries => _entries;

	public int Count => _entries.Count;

	public object? Top => _entries.Count == 0 ? null : _entries[^1];

	public bool Contains(object platformView) => _entries.Contains(platformView);

	public bool IsDisposed(object platformView) =>
		platformView switch
		{
			FakePlaceholder placeholder => placeholder.Disposed,
			FakeModalPageRealizer.FakePlatformView view => view.Disposed,
			_ => false,
		};

	public bool ShownBehindPage
	{
		get => _shownBehindPage;
		set
		{
			_shownBehindPage = value;
			ShownBehindPageWrites.Add(value);
		}
	}

	public List<bool> ShownBehindPageWrites { get; } = new();

	public Exception? PushFailure { get; set; }

	public Exception? PopFailure { get; set; }

	public bool MutateBeforePushFailure { get; set; }

	public bool MutateBeforePopFailure { get; set; }

	public bool RemoveBeforePopFailureWithoutDisposal { get; set; }

	public int PopFailuresBeforeMutationRemaining { get; set; }

	public HashSet<object> RemoveFailures { get; } = new();

	public bool FailEveryRemove { get; set; }

	public TaskCompletionSource<object?>? PushBlocker { get; set; }

	public TaskCompletionSource<object?>? PushAfterInsertionBlocker { get; set; }

	public TaskCompletionSource<object?>? PopBlocker { get; set; }

	public TaskCompletionSource<object?> PushStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public TaskCompletionSource<object?> PushInserted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public TaskCompletionSource<object?> PopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Queue<TaskCompletionSource<object?>> PushBlockers { get; } = new();

	public Queue<TaskCompletionSource<object?>> PushAfterInsertionBlockers { get; } = new();

	public Queue<Exception?> PushFailures { get; } = new();

	public int PushStartedCount { get; private set; }

	public int PushInsertedCount { get; private set; }

	public bool InsertedDisposedPlaceholder { get; private set; }

	public List<FakePlaceholder> Placeholders { get; } = new();

	public object CreatePlaceholder()
	{
		Operations.Add("CreatePlaceholder");
		var placeholder = new FakePlaceholder();
		Placeholders.Add(placeholder);
		return placeholder;
	}

	/// <summary>
	/// When true, push and pop complete on a later turn of the scheduler. A caller that does not
	/// await them then observes a stack that has not changed yet, which is what makes
	/// fire-and-forget bugs visible.
	/// </summary>
	public bool CompleteAsynchronously { get; set; }

	/// <summary>Records ShownBehindPage as observed at the moment of each push.</summary>
	public List<bool> ShownBehindPageDuringPush { get; } = new();

	public async Task PushAsync(object platformView, bool animated)
	{
		Operations.Add($"Push({animated})");
		PushAnimations.Add(animated);
		ShownBehindPageDuringPush.Add(ShownBehindPage);
		PushStartedCount++;
		PushStarted.TrySetResult(null);
		var pushFailure = PushFailures.Count > 0 ? PushFailures.Dequeue() : PushFailure;

		if (CompleteAsynchronously)
		{
			await Task.Yield();
		}

		var pushBlocker = PushBlockers.Count > 0 ? PushBlockers.Dequeue() : PushBlocker;
		if (pushBlocker is not null)
		{
			await pushBlocker.Task;
		}

		if (pushFailure is not null && !MutateBeforePushFailure)
		{
			throw pushFailure;
		}

		InsertedDisposedPlaceholder |= platformView is FakePlaceholder placeholder && placeholder.Disposed;
		_entries.Add(platformView);
		PushInsertedCount++;
		PushInserted.TrySetResult(null);

		var insertionBlocker = PushAfterInsertionBlockers.Count > 0
			? PushAfterInsertionBlockers.Dequeue()
			: PushAfterInsertionBlocker;
		if (insertionBlocker is not null)
		{
			await insertionBlocker.Task;
		}

		if (pushFailure is not null)
		{
			throw pushFailure;
		}
	}

	public async Task PopAsync(bool animated)
	{
		Operations.Add($"Pop({animated})");
		PopAnimations.Add(animated);

		if (PopBlocker is not null)
		{
			PopStarted.TrySetResult(null);
			await PopBlocker.Task;
		}

		if (PopFailure is not null
			&& (!MutateBeforePopFailure || PopFailuresBeforeMutationRemaining > 0))
		{
			if (PopFailuresBeforeMutationRemaining > 0)
			{
				PopFailuresBeforeMutationRemaining--;
			}

			throw PopFailure;
		}

		if (_entries.Count > 0)
		{
			var top = _entries[^1];
			_entries.RemoveAt(_entries.Count - 1);

			if (!RemoveBeforePopFailureWithoutDisposal)
			{
				(top as IDisposable)?.Dispose();
			}
		}

		if (PopFailure is not null)
		{
			throw PopFailure;
		}
	}

	public bool Remove(object platformView)
	{
		Operations.Add("Remove");

		if (FailEveryRemove || RemoveFailures.Contains(platformView))
		{
			throw new InvalidOperationException("remove failed");
		}

		var isTop = ReferenceEquals(Top, platformView);
		var removed = _entries.Remove(platformView);

		if (removed && isTop)
		{
			(platformView as IDisposable)?.Dispose();
		}

		return removed && isTop;
	}
}

internal sealed class FakePlaceholder : IDisposable
{
	public bool Disposed => DisposeCount > 0;
	public int DisposeCount { get; private set; }

	public void Dispose() => DisposeCount++;
}

/// <summary>
/// A stand-in for the framework's modal navigation host from dotnet/maui#37853.
/// </summary>
internal sealed class FakeModalNavigationHost : IModalNavigationHost
{
	readonly List<Page> _platformModalStack = new();

	public FakeModalNavigationHost(IMauiContext mauiContext, Window? window = null)
	{
		MauiContext = mauiContext;
		Window = window ?? new Window();
	}

	public Window Window { get; }

	public IMauiContext MauiContext { get; }

	public IReadOnlyList<Page> PlatformModalStack => _platformModalStack;

	public Page? CurrentPage { get; set; }

	public Page CurrentPlatformPage =>
		_platformModalStack.Count > 0
			? _platformModalStack[^1]
			: CurrentPage ?? throw new InvalidOperationException("No page.");

	public bool IsWindowReady { get; set; } = true;

	public bool IsBatchPushing { get; set; }

	public bool IsBatchPopping { get; set; }

	public int RequestSyncCount { get; private set; }

	public void RequestSync() => RequestSyncCount++;

	/// <summary>Mirrors the framework adding to the platform stack before awaiting a push.</summary>
	public void RecordPush(Page modal) => _platformModalStack.Add(modal);

	/// <summary>Mirrors the framework removing from the platform stack before awaiting a pop.</summary>
	public void RecordPop(Page modal) => _platformModalStack.Remove(modal);
}

/// <summary>
/// Records the pages that were realized and released.
/// </summary>
internal sealed class FakeModalPageRealizer : ITizenModalPageRealizer
{
	readonly Dictionary<Page, FakePlatformView> _platformViews = new();

	public List<Page> Realized { get; } = new();

	public List<Page> Released { get; } = new();
	public List<(Page Page, bool PlatformViewDisposed)> Releases { get; } = new();
	public List<(Page Page, bool PlatformViewDisposed)> ReleaseAttempts { get; } = new();
	public Dictionary<Page, Exception> ReleaseFailures { get; } = new();
	public Action<Page, object, bool>? BeforeRelease { get; set; }

	public object Realize(Page page, IMauiContext mauiContext)
	{
		Realized.Add(page);
		var platformView = new FakePlatformView();
		_platformViews[page] = platformView;
		return platformView;
	}

	public void Release(Page page, object platformView, bool platformViewDisposed)
	{
		ReleaseAttempts.Add((page, platformViewDisposed));
		BeforeRelease?.Invoke(page, platformView, platformViewDisposed);

		if (ReleaseFailures.TryGetValue(page, out var failure))
		{
			throw failure;
		}

		Released.Add(page);
		Releases.Add((page, platformViewDisposed));

		if (!platformViewDisposed)
		{
			(platformView as IDisposable)?.Dispose();
		}
	}

	public object PlatformViewFor(Page page) => _platformViews[page];

	public int DisposeCountFor(Page page) => _platformViews[page].DisposeCount;

	internal sealed class FakePlatformView : IDisposable
	{
		public bool Disposed => DisposeCount > 0;
		public int DisposeCount { get; private set; }

		public void Dispose() => DisposeCount++;
	}
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
	public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();

	public bool ThrowOnLog { get; set; }

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		if (ThrowOnLog)
		{
			throw new InvalidOperationException("logger failed");
		}

		Entries.Add((logLevel, exception, formatter(state, exception)));
	}
}

internal sealed class FakeWindowBackButton : ITizenWindowBackButton
{
	public Func<bool>? Handler { get; private set; }

	public int SetCount { get; private set; }
	public Func<bool>? FallbackHandler { get; set; }

	public IDisposable RegisterBackButtonPressedHandler(Func<bool> handler)
	{
		Handler = handler;
		SetCount++;
		return new Registration(this, handler);
	}

	public bool Invoke() => Handler?.Invoke() == true || FallbackHandler?.Invoke() == true;

	sealed class Registration : IDisposable
	{
		FakeWindowBackButton? _owner;
		readonly Func<bool> _handler;

		public Registration(FakeWindowBackButton owner, Func<bool> handler)
		{
			_owner = owner;
			_handler = handler;
		}

		public void Dispose()
		{
			var owner = _owner;
			_owner = null;
			if (owner is not null && ReferenceEquals(owner.Handler, _handler))
			{
				owner.Handler = null;
			}
		}
	}
}
