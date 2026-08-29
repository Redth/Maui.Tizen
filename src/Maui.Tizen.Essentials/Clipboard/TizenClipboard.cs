using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IClipboard"/>, backed by
	/// <see cref="global::Tizen.NUI.Clipboard"/>.
	/// </summary>
	public sealed class TizenClipboard : IClipboard, IDisposable
	{
		readonly object _locker = new();
		readonly ITizenClipboardDispatcher _dispatcher;
		readonly ITizenClipboardNative _native;
		readonly TizenEventSubscriptionCoordinator<EventArgs> _events;
		readonly Dictionary<long, TaskCompletionSource<string?>> _pending = [];
		long _nextRequest;
		bool _disposed;

		/// <summary>Maximum time to wait for Tizen's one-shot clipboard callback.</summary>
		internal static TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

		/// <summary>Creates a clipboard service using the NUI singleton.</summary>
		public TizenClipboard()
			: this(TizenClipboardDispatcher.Instance, TizenClipboardNative.Instance)
		{
		}

		internal TizenClipboard(
			ITizenClipboardDispatcher dispatcher,
			ITizenClipboardNative native)
		{
			_dispatcher = dispatcher;
			_native = native;
			_events = new(
				this,
				generation =>
				{
					Action nativeCallback = () => generation.Publish(EventArgs.Empty);
					_dispatcher.Invoke(() => _native.StartChangeNotifications(nativeCallback));
					return () => _dispatcher.Invoke(_native.StopChangeNotifications);
				},
				new TizenNativeCallbackCoordinator(dispatcher));
		}

		/// <inheritdoc/>
		/// <remarks>
		/// NUI exposes text reads only as an asynchronous callback. Blocking the NUI thread to
		/// implement this synchronous property would deadlock the callback, so this member alone is
		/// unsupported; <see cref="GetTextAsync"/> and <see cref="SetTextAsync"/> are implemented.
		/// </remarks>
		public bool HasText =>
			throw TizenEssentialsSupport.NotSupported(
				$"{nameof(IClipboard)}.{nameof(HasText)}",
				"Tizen.NUI.Clipboard exposes reads only through an asynchronous callback; the " +
				"synchronous HasText contract cannot be implemented without blocking the NUI loop.");

		/// <inheritdoc/>
		public event EventHandler<EventArgs> ClipboardContentChanged
		{
			add => _events.Add(value);
			remove => _events.Remove(value);
		}

		/// <inheritdoc/>
		public Task<string?> GetTextAsync() => GetTextAsync(CancellationToken.None);

		internal async Task<string?> GetTextAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			TaskCompletionSource<string?> completion;
			long request;
			lock (_locker)
			{
				ObjectDisposedException.ThrowIf(_disposed, this);
				request = ++_nextRequest;
				completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
				_pending.Add(request, completion);
			}

			using var timeout = new CancellationTokenSource(RequestTimeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				timeout.Token);
			using var registration = linked.Token.Register(() =>
			{
				TaskCompletionSource<string?>? pending;
				lock (_locker)
				{
					if (!_pending.Remove(request, out pending))
						return;
				}

				if (cancellationToken.IsCancellationRequested)
					pending.TrySetCanceled(cancellationToken);
				else
					pending.TrySetException(
						new TimeoutException(
							$"Tizen did not return clipboard text within {RequestTimeout}."));
			});

			try
			{
				await _dispatcher.InvokeAsync(() =>
				{
					lock (_locker)
					{
						if (_disposed || !_pending.ContainsKey(request))
							return;
					}

					_native.GetText((success, text) => CompleteRequest(request, success, text));
				})
					.ConfigureAwait(false);
			}
			catch
			{
				lock (_locker)
					_pending.Remove(request);
				throw;
			}

			return await completion.Task.ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task SetTextAsync(string? text)
		{
			lock (_locker)
				ObjectDisposedException.ThrowIf(_disposed, this);

			var accepted = await _dispatcher.InvokeAsync(() =>
			{
				lock (_locker)
					ObjectDisposedException.ThrowIf(_disposed, this);
				return _native.SetText(text ?? string.Empty);
			}).ConfigureAwait(false);

			if (!accepted)
				throw new InvalidOperationException("Tizen rejected the clipboard text request.");
		}

		void CompleteRequest(long request, bool success, string? text)
		{
			TaskCompletionSource<string?>? completion;
			lock (_locker)
			{
				if (!_pending.Remove(request, out completion))
					return;
			}

			if (success)
				completion.TrySetResult(text);
			else
				completion.TrySetResult(null);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			List<TaskCompletionSource<string?>> pending;
			lock (_locker)
			{
				if (_disposed)
					return;

				_disposed = true;
				pending = [.. _pending.Values];
				_pending.Clear();
			}

			foreach (var completion in pending)
				completion.TrySetException(new ObjectDisposedException(nameof(TizenClipboard)));

			_events.Dispose();
		}
	}

	internal interface ITizenClipboardDispatcher : ITizenNativeCallbackDispatcher
	{
		void Invoke(Action action);

		Task InvokeAsync(Action action);

		Task<T> InvokeAsync<T>(Func<T> action);

	}

	internal interface ITizenClipboardNative
	{
		void StartChangeNotifications(Action changed);

		void StopChangeNotifications();

		bool SetText(string text);

		void GetText(Action<bool, string?> callback);
	}

	sealed class TizenClipboardDispatcher : ITizenClipboardDispatcher
	{
		public static TizenClipboardDispatcher Instance { get; } = new();

		public void Invoke(Action action)
		{
			if (MainThread.IsMainThread)
				action();
			else
				MainThread.InvokeOnMainThreadAsync(action).GetAwaiter().GetResult();
		}

		public Task InvokeAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);

		public Task<T> InvokeAsync<T>(Func<T> action) => MainThread.InvokeOnMainThreadAsync(action);

		public void PostDeferred(Action action) =>
			TizenNativeCallbackDispatcher.PostDeferred(
				MainThread.BeginInvokeOnMainThread,
				static () => SynchronizationContext.Current,
				action);
	}

	sealed class TizenClipboardNative : ITizenClipboardNative
	{
		const string MimeType = "text/plain;charset=utf-8";
		global::Tizen.NUI.Clipboard? _clipboard;
		global::Tizen.NUI.WindowSystem.Shell.TizenShell? _shell;
		global::Tizen.NUI.WindowSystem.Shell.KVMService? _kvm;
		EventHandler<global::Tizen.NUI.ClipboardDataSelectedEventArgs>? _dataSelectedHandler;

		public static TizenClipboardNative Instance { get; } = new();

		TizenClipboardNative()
		{
		}

		global::Tizen.NUI.Clipboard Clipboard =>
			_clipboard ??= global::Tizen.NUI.Clipboard.Instance;

		public void StartChangeNotifications(Action changed)
		{
			if (_kvm is not null)
				throw new InvalidOperationException("Clipboard change notifications are already active.");

			global::Tizen.NUI.WindowSystem.Shell.TizenShell? shell = null;
			global::Tizen.NUI.WindowSystem.Shell.KVMService? kvm = null;
			EventHandler<global::Tizen.NUI.ClipboardDataSelectedEventArgs>? dataSelectedHandler = null;
			var selected = false;
			var subscribed = false;
			try
			{
				shell = new global::Tizen.NUI.WindowSystem.Shell.TizenShell();
				kvm = new global::Tizen.NUI.WindowSystem.Shell.KVMService(
					shell,
					global::Tizen.NUI.Window.Default);
				kvm.SetSecondarySelection();
				selected = true;
				dataSelectedHandler = (_, _) => changed();
				Clipboard.DataSelected += dataSelectedHandler;
				subscribed = true;
				_shell = shell;
				_kvm = kvm;
				_dataSelectedHandler = dataSelectedHandler;
			}
			catch
			{
				if (subscribed && dataSelectedHandler is not null)
				{
				try
				{
					Clipboard.DataSelected -= dataSelectedHandler;
				}
				catch (Exception)
				{
				}
				}
				if (selected)
				{
				try
				{
					kvm?.UnsetSecondarySelection();
				}
				catch (Exception)
				{
				}
				}
				try
				{
				kvm?.Dispose();
				}
				catch (Exception)
				{
				}
				try
				{
				shell?.Dispose();
				}
				catch (Exception)
				{
				}
				throw;
			}
		}

		public void StopChangeNotifications()
		{
			if (_kvm is null && _dataSelectedHandler is null)
				return;

			var failures = new List<Exception>();
			if (_dataSelectedHandler is not null)
			{
				try
				{
					Clipboard.DataSelected -= _dataSelectedHandler;
				}
				catch (Exception exception)
				{
					failures.Add(exception);
				}
			}

			try
			{
				_kvm?.UnsetSecondarySelection();
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}

			try
			{
				_kvm?.Dispose();
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}

			try
			{
				_shell?.Dispose();
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}

			_dataSelectedHandler = null;
			_kvm = null;
			_shell = null;

			if (failures.Count == 1)
				throw failures[0];
			if (failures.Count > 1)
				throw new AggregateException("Clipboard change notification teardown failed.", failures);
		}

		public bool SetText(string text) => Clipboard.SetData(MimeType, text);

		public void GetText(Action<bool, string?> callback) =>
			Clipboard.GetData(
				MimeType,
				(success, clipEvent) => callback(success, success ? clipEvent.Data : null));

	}
}
