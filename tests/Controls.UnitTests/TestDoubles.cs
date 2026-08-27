using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// A dialog whose result is driven by the test rather than by a user.
/// </summary>
internal sealed class FakeAlertDialog<TResult> : ITizenAlertDialog<TResult>
{
	readonly TaskCompletionSource<TResult> _completion;

	public FakeAlertDialog(bool runContinuationsSynchronously = false)
	{
		_completion = runContinuationsSynchronously
			? new TaskCompletionSource<TResult>()
			: new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	public bool Opened { get; private set; }

	public bool Closed { get; private set; }

	public bool Disposed { get; private set; }

	public Exception? CloseFailure { get; set; }

	public Exception? DisposeFailure { get; set; }

	public int DisposeCount { get; private set; }

	public Task<TResult> OpenAsync()
	{
		Opened = true;
		return _completion.Task;
	}

	public void Close()
	{
		Closed = true;

		if (CloseFailure is not null)
		{
			throw CloseFailure;
		}

		_completion.TrySetCanceled();
	}

	public void Dispose()
	{
		Disposed = true;
		DisposeCount++;

		if (DisposeFailure is not null)
		{
			throw DisposeFailure;
		}
	}

	public void CompleteWith(TResult result) => _completion.TrySetResult(result);

	public void FailWith(Exception exception) => _completion.TrySetException(exception);
}

/// <summary>
/// Hands out <see cref="FakeAlertDialog{TResult}"/> instances and records what was requested.
/// </summary>
internal sealed class FakeAlertDialogFactory : ITizenAlertDialogFactory
{
	public Action? BeforeCreateAlertDialog { get; set; }

	public Exception? AlertDialogDisposeFailure { get; set; }

	/// <summary>
	/// Runs dialog task continuations inline, matching NUI popup close ordering.
	/// </summary>
	public bool UseSynchronousDialogContinuations { get; set; }

	public List<AlertArguments> AlertRequests { get; } = new();

	public List<ActionSheetArguments> ActionSheetRequests { get; } = new();

	public List<PromptArguments> PromptRequests { get; } = new();

	public FakeAlertDialog<bool>? LastAlert { get; private set; }

	public FakeAlertDialog<string?>? LastActionSheet { get; private set; }

	public FakeAlertDialog<string?>? LastPrompt { get; private set; }

	public FakeBusyIndicator? LastBusyIndicator { get; private set; }

	public int BusyIndicatorsCreated { get; private set; }

	public ITizenAlertDialog<bool> CreateAlertDialog(AlertArguments arguments)
	{
		BeforeCreateAlertDialog?.Invoke();
		AlertRequests.Add(arguments);
		return LastAlert = new FakeAlertDialog<bool>(UseSynchronousDialogContinuations)
		{
			DisposeFailure = AlertDialogDisposeFailure,
		};
	}

	public ITizenAlertDialog<string?> CreateActionSheetDialog(ActionSheetArguments arguments)
	{
		ActionSheetRequests.Add(arguments);
		return LastActionSheet = new FakeAlertDialog<string?>(UseSynchronousDialogContinuations);
	}

	public ITizenAlertDialog<string?> CreatePromptDialog(PromptArguments arguments)
	{
		PromptRequests.Add(arguments);
		return LastPrompt = new FakeAlertDialog<string?>(UseSynchronousDialogContinuations);
	}

	public ITizenBusyIndicator CreateBusyIndicator()
	{
		BusyIndicatorsCreated++;
		return LastBusyIndicator = new FakeBusyIndicator();
	}
}

internal sealed class FakeBusyIndicator : ITizenBusyIndicator
{
	public bool IsOpen { get; private set; }

	public int OpenCount { get; private set; }

	public int CloseCount { get; private set; }

	public bool Disposed { get; private set; }

	public Exception? CloseFailure { get; set; }

	public void Open()
	{
		if (IsOpen)
		{
			return;
		}

		IsOpen = true;
		OpenCount++;
	}

	public void Close()
	{
		if (CloseFailure is not null)
		{
			throw CloseFailure;
		}

		if (!IsOpen)
		{
			return;
		}

		IsOpen = false;
		CloseCount++;
	}

	public void Dispose() => Disposed = true;
}

/// <summary>
/// Records modal-stack usage so tests can assert that dialogs are pushed and popped in balance.
/// </summary>
internal sealed class FakeModalHost : ITizenModalHost
{
	public int Entered { get; private set; }

	public int Exited { get; private set; }

	public bool IsBalanced => Entered == Exited;

	public async Task RunModalAsync(Func<Task> dialogOperation)
	{
		Entered++;

		try
		{
			await dialogOperation();
		}
		finally
		{
			Exited++;
		}
	}
}

/// <summary>
/// A modal host that faults, standing in for a modal stack torn down under the dialog.
/// </summary>
internal sealed class ThrowingModalHost : ITizenModalHost
{
	readonly Exception _exception;

	public ThrowingModalHost(Exception exception) => _exception = exception;

	public Task RunModalAsync(Func<Task> dialogOperation) => Task.FromException(_exception);
}

/// <summary>
/// Maps contexts to windows using an explicit table, so window affinity can be exercised without NUI.
/// </summary>
internal sealed class FakeWindowProvider : ITizenPlatformWindowProvider
{
	readonly Dictionary<IMauiContext, object> _windows = new();

	public void Map(IMauiContext context, object window) => _windows[context] = window;

	public object? GetPlatformWindow(IMauiContext? context) =>
		context is not null && _windows.TryGetValue(context, out var window) ? window : null;
}

/// <summary>
/// A native detector whose events are raised by the test.
/// </summary>
internal sealed class FakeNativeGestureDetector : ITizenNativeGestureDetector
{
	public event EventHandler<TizenGestureEventArgs>? Detected;

	public bool IsAttached { get; private set; }

	public bool Disposed { get; private set; }

	public object? AttachedView { get; private set; }

	public int AttachCount { get; private set; }

	public int DetachCount { get; private set; }

	public void Attach(object platformView)
	{
		if (IsAttached)
		{
			return;
		}

		IsAttached = true;
		AttachedView = platformView;
		AttachCount++;
	}

	public void Detach()
	{
		if (!IsAttached)
		{
			return;
		}

		IsAttached = false;
		AttachedView = null;
		DetachCount++;
	}

	public void Dispose() => Disposed = true;

	public TizenGestureEventArgs Raise(TizenGestureEventArgs args)
	{
		Detected?.Invoke(this, args);
		return args;
	}
}

internal sealed class FakeNativeGestureDetectorFactory : ITizenNativeGestureDetectorFactory
{
	readonly HashSet<TizenGestureKind> _unsupported;

	public FakeNativeGestureDetectorFactory(params TizenGestureKind[] unsupported) =>
		_unsupported = new HashSet<TizenGestureKind>(unsupported);

	public List<FakeNativeGestureDetector> Created { get; } = new();

	public ITizenNativeGestureDetector? CreateDetector(TizenGestureKind kind, IGestureRecognizer recognizer)
	{
		if (_unsupported.Contains(kind))
		{
			return null;
		}

		var detector = new FakeNativeGestureDetector();
		Created.Add(detector);
		return detector;
	}
}

/// <summary>
/// Records every dispatch so gesture translation can be asserted without .NET MAUI's internal
/// gesture plumbing.
/// </summary>
internal sealed class RecordingGestureDispatcher : ITizenGestureDispatcher
{
	public List<string> Calls { get; } = new();

	public List<(TizenGestureState State, double X, double Y, int GestureId)> Pans { get; } = new();

	public List<(TizenGestureState State, double Scale, Point Origin)> Pinches { get; } = new();

	public List<(TizenGestureState State, double X, double Y)> Swipes { get; } = new();

	public List<(TizenGestureState State, Point Position)> LongPresses { get; } = new();

	public List<(TizenPointerAction Action, Point Position)> Pointers { get; } = new();

	public List<Point> Taps { get; } = new();

	public List<TizenGesturePosition> TapPositions { get; } = new();

	public List<TizenGesturePosition> PointerPositions { get; } = new();

	public List<TizenPointerButton> Buttons { get; } = new();

	public bool IsSupported(TizenGestureKind kind) => true;

	public void SendTapped(TapGestureRecognizer recognizer, View view, TizenGesturePosition position, TizenPointerButton button)
	{
		Calls.Add($"Tap:{position.Local.X},{position.Local.Y}");
		Taps.Add(position.Local);
		TapPositions.Add(position);
		Buttons.Add(button);
	}

	public void SendPan(PanGestureRecognizer recognizer, View view, TizenGestureState state, double totalX, double totalY, int gestureId)
	{
		Calls.Add($"Pan:{state}:{totalX},{totalY}:{gestureId}");
		Pans.Add((state, totalX, totalY, gestureId));
	}

	public void SendPinch(PinchGestureRecognizer recognizer, View view, TizenGestureState state, double scale, Point origin)
	{
		Calls.Add($"Pinch:{state}:{scale}");
		Pinches.Add((state, scale, origin));
	}

	public void SendSwipe(SwipeGestureRecognizer recognizer, View view, TizenGestureState state, double totalX, double totalY)
	{
		Calls.Add($"Swipe:{state}:{totalX},{totalY}");
		Swipes.Add((state, totalX, totalY));
	}

	public void SendLongPress(LongPressGestureRecognizer recognizer, View view, TizenGestureState state, TizenGesturePosition position)
	{
		Calls.Add($"LongPress:{state}");
		LongPresses.Add((state, position.Local));
	}

	public void SendPointer(PointerGestureRecognizer recognizer, View view, TizenPointerAction action, TizenGesturePosition position, TizenPointerButton button)
	{
		Calls.Add($"Pointer:{action}");
		Pointers.Add((action, position.Local));
		PointerPositions.Add(position);
		Buttons.Add(button);
	}
}
