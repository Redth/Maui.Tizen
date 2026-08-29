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
		readonly Dictionary<long, TaskCompletionSource<string?>> _pending = [];
		EventHandler<EventArgs>? _clipboardContentChanged;
		long _nextRequest;
		bool _listening;
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
			add
			{
				ArgumentNullException.ThrowIfNull(value);
				lock (_locker)
				{
					ObjectDisposedException.ThrowIf(_disposed, this);
					var start = _clipboardContentChanged is null;
					_clipboardContentChanged += value;
					if (!start)
						return;

					try
					{
						_dispatcher.Invoke(() => _native.DataSelected += OnDataSelected);
						_listening = true;
					}
					catch
					{
						_clipboardContentChanged -= value;
						throw;
					}
				}
			}
			remove
			{
				lock (_locker)
				{
					_clipboardContentChanged -= value;
					if (_clipboardContentChanged is not null || !_listening)
						return;

					_dispatcher.Invoke(() => _native.DataSelected -= OnDataSelected);
					_listening = false;
				}
			}
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
					if (!linked.IsCancellationRequested)
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

		void OnDataSelected()
		{
			_dispatcher.Post(() =>
			{
				EventHandler<EventArgs>? handler;
				lock (_locker)
				{
					if (_disposed)
						return;
					handler = _clipboardContentChanged;
				}

				handler?.Invoke(this, EventArgs.Empty);
			});
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			List<TaskCompletionSource<string?>> pending;
			var stopListening = false;

			lock (_locker)
			{
				if (_disposed)
					return;

				_disposed = true;
				stopListening = _listening;
				_listening = false;
				_clipboardContentChanged = null;
				pending = [.. _pending.Values];
				_pending.Clear();
			}

			if (stopListening)
			{
				try
				{
					_dispatcher.Invoke(() => _native.DataSelected -= OnDataSelected);
				}
				catch (Exception)
				{
					// The owning app is already tearing down; pending callers are still settled.
				}
			}

			foreach (var completion in pending)
				completion.TrySetException(new ObjectDisposedException(nameof(TizenClipboard)));
		}
	}

	internal interface ITizenClipboardDispatcher
	{
		void Invoke(Action action);

		Task InvokeAsync(Action action);

		Task<T> InvokeAsync<T>(Func<T> action);

		void Post(Action action);
	}

	internal interface ITizenClipboardNative
	{
		event Action? DataSelected;

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

		public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);
	}

	sealed class TizenClipboardNative : ITizenClipboardNative
	{
		const string MimeType = "text/plain;charset=utf-8";
		global::Tizen.NUI.Clipboard? _clipboard;
		Action? _dataSelected;

		public static TizenClipboardNative Instance { get; } = new();

		TizenClipboardNative()
		{
		}

		global::Tizen.NUI.Clipboard Clipboard =>
			_clipboard ??= global::Tizen.NUI.Clipboard.Instance;

		public event Action? DataSelected
		{
			add
			{
				if (_dataSelected is null)
					Clipboard.DataSelected += OnDataSelected;
				_dataSelected += value;
			}
			remove
			{
				_dataSelected -= value;
				if (_dataSelected is null)
					Clipboard.DataSelected -= OnDataSelected;
			}
		}

		public bool SetText(string text) => Clipboard.SetData(MimeType, text);

		public void GetText(Action<bool, string?> callback) =>
			Clipboard.GetData(
				MimeType,
				(success, clipEvent) => callback(success, success ? clipEvent.Data : null));

		void OnDataSelected(object? sender, global::Tizen.NUI.ClipboardDataSelectedEventArgs e) =>
			_dataSelected?.Invoke();
	}
}
