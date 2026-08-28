using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal sealed class TizenDelegateAlertManagerSubscription : IAlertManagerSubscription, IDisposable
	{
		internal const string DisplayAlertServiceKey = "Microsoft.Maui.Controls.DisplayAlert";
		internal const string DisplayActionSheetServiceKey = "Microsoft.Maui.Controls.DisplayActionSheet";
		internal const string DisplayPromptServiceKey = "Microsoft.Maui.Controls.DisplayPrompt";

		readonly Func<Page, AlertArguments, Task<bool>>? _alertHandler;
		readonly Func<Page, ActionSheetArguments, Task<string?>>? _actionSheetHandler;
		readonly Func<Page, PromptArguments, Task<string?>>? _promptHandler;
		readonly Func<IAlertManagerSubscription> _createFallback;
		readonly object _sync = new();

		IAlertManagerSubscription? _fallback;
		bool _disposed;

		public TizenDelegateAlertManagerSubscription(
			Func<Page, AlertArguments, Task<bool>>? alertHandler,
			Func<Page, ActionSheetArguments, Task<string?>>? actionSheetHandler,
			Func<Page, PromptArguments, Task<string?>>? promptHandler,
			Func<IAlertManagerSubscription> createFallback)
		{
			_alertHandler = alertHandler;
			_actionSheetHandler = actionSheetHandler;
			_promptHandler = promptHandler;
			_createFallback = createFallback ?? throw new ArgumentNullException(nameof(createFallback));
		}

		public void OnAlertRequested(Page sender, AlertArguments arguments)
		{
			if (_alertHandler is null)
			{
				InvokeFallback(fallback => fallback.OnAlertRequested(sender, arguments));

				return;
			}

			Invoke(() => _alertHandler(sender, arguments), arguments.Result);
		}

		public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments)
		{
			if (_actionSheetHandler is null)
			{
				InvokeFallback(fallback => fallback.OnActionSheetRequested(sender, arguments));

				return;
			}

			Invoke(() => _actionSheetHandler(sender, arguments), arguments.Result);
		}

		public void OnPromptRequested(Page sender, PromptArguments arguments)
		{
			if (_promptHandler is null)
			{
				InvokeFallback(fallback => fallback.OnPromptRequested(sender, arguments));

				return;
			}

			Invoke(() => _promptHandler(sender, arguments), arguments.Result);
		}

		[Obsolete("Page busy notifications are obsolete and have no replacement. Remove usage. This method will be removed in a future release.")]
		public void OnPageBusy(Page sender, bool enabled)
		{
#pragma warning disable CS0618 // Deliberately forwarding the obsolete notification.
			InvokeFallback(fallback => fallback.OnPageBusy(sender, enabled));
#pragma warning restore CS0618
		}

		public void Dispose()
		{
			IDisposable? disposable;

			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				disposable = _fallback as IDisposable;
				_fallback = null;
			}

			disposable?.Dispose();
		}

		void InvokeFallback(Action<IAlertManagerSubscription> invoke)
		{
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}

				invoke(_fallback ??= _createFallback());
			}
		}

		static void Invoke<TResult>(
			Func<Task<TResult>> invoker,
			TaskCompletionSource<TResult> completion)
		{
			Task<TResult>? task;

			try
			{
				task = invoker();
			}
			catch (OperationCanceledException ex)
			{
				completion.TrySetCanceled(ex.CancellationToken);
				return;
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
				return;
			}

			if (task is null)
			{
				completion.TrySetException(
					new InvalidOperationException(
						"Dialog delegate returned a null Task. The delegate must return a non-null Task containing the dialog result."));
				return;
			}

			if (task.IsCompleted)
			{
				ForwardCompletion(task, completion);
				return;
			}

			_ = task.ContinueWith(
				static (completed, state) =>
					ForwardCompletion(completed, (TaskCompletionSource<TResult>)state!),
				completion,
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		static void ForwardCompletion<TResult>(
			Task<TResult> task,
			TaskCompletionSource<TResult> completion)
		{
			if (task.IsFaulted)
			{
				completion.TrySetException(task.Exception.InnerExceptions);
			}
			else if (task.IsCanceled)
			{
				try
				{
					task.GetAwaiter().GetResult();
				}
				catch (OperationCanceledException ex)
				{
					completion.TrySetCanceled(ex.CancellationToken);
					return;
				}

				completion.TrySetCanceled();
			}
			else
			{
				completion.TrySetResult(task.Result);
			}
		}
	}
}
