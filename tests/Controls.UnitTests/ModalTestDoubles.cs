using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// An in-memory navigation stack that records the operations performed on it.
/// </summary>
internal sealed class FakeNavigationStack : ITizenNavigationStack
{
	readonly List<object> _entries = new();

	public List<string> Operations { get; } = new();

	public List<bool> PushAnimations { get; } = new();

	public List<bool> PopAnimations { get; } = new();

	public IReadOnlyList<object> Entries => _entries;

	public int Count => _entries.Count;

	public object? Top => _entries.Count == 0 ? null : _entries[^1];

	public bool Contains(object platformView) => _entries.Contains(platformView);

	public bool ShownBehindPage { get; set; }

	public List<bool> ShownBehindPageWrites { get; } = new();

	public Exception? PushFailure { get; set; }

	public Exception? PopFailure { get; set; }

	public bool MutateBeforePushFailure { get; set; }

	public bool MutateBeforePopFailure { get; set; }

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

		if (CompleteAsynchronously)
		{
			await Task.Yield();
		}

		if (PushFailure is not null && !MutateBeforePushFailure)
		{
			throw PushFailure;
		}

		_entries.Add(platformView);

		if (PushFailure is not null)
		{
			throw PushFailure;
		}
	}

	public Task PopAsync(bool animated)
	{
		Operations.Add($"Pop({animated})");
		PopAnimations.Add(animated);

		if (PopFailure is not null && !MutateBeforePopFailure)
		{
			return Task.FromException(PopFailure);
		}

		if (_entries.Count > 0)
		{
			_entries.RemoveAt(_entries.Count - 1);
		}

		return PopFailure is null ? Task.CompletedTask : Task.FromException(PopFailure);
	}

	public void Remove(object platformView)
	{
		Operations.Add("Remove");
		_entries.Remove(platformView);
	}
}

internal sealed class FakePlaceholder : IDisposable
{
	public bool Disposed { get; private set; }

	public void Dispose() => Disposed = true;
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

	public bool IsModalReady { get; set; } = true;

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
	readonly Dictionary<Page, object> _platformViews = new();

	public List<Page> Realized { get; } = new();

	public List<Page> Released { get; } = new();

	public object Realize(Page page, IMauiContext mauiContext)
	{
		Realized.Add(page);
		var platformView = new object();
		_platformViews[page] = platformView;
		return platformView;
	}

	public void Release(Page page) => Released.Add(page);

	public object PlatformViewFor(Page page) => _platformViews[page];
}

internal sealed class FakeWindowBackButton : ITizenWindowBackButton
{
	public Func<bool>? Handler { get; private set; }

	public int SetCount { get; private set; }

	public void SetBackButtonPressedHandler(Func<bool>? handler)
	{
		Handler = handler;
		SetCount++;
	}
}
