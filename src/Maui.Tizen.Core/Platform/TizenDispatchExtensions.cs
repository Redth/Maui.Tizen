// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Marshals handler continuations back onto the Tizen main loop.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Anything a handler does after an <c>await</c> - touching a NUI view, or writing a property
	/// on the virtual view - has to happen on the UI thread. NUI is not thread-safe, and a write
	/// to the virtual view re-enters MAUI's property system, which runs the mapper and touches NUI
	/// too. Neither failure is reliably visible in a test: off-thread NUI access usually works
	/// until it corrupts state or crashes under load.
	/// </para>
	/// <para>
	/// <c>ConfigureAwait(false)</c> is therefore wrong in handler code, even though it is the right
	/// default in library code. The awaits here are deliberately configured to resume on the
	/// captured context where one exists, and explicitly re-dispatched where it may not.
	/// </para>
	/// </remarks>
	public static class TizenDispatchExtensions
	{
		/// <summary>
		/// Runs <paramref name="action"/> on the UI thread, immediately if already on it.
		/// </summary>
		/// <remarks>
		/// Dispatching unconditionally would defer work that could have run inline, turning a
		/// synchronous property application into a queued one and reordering it against the
		/// mapper pass that follows.
		/// </remarks>
		/// <param name="handler">The handler whose dispatcher should be used.</param>
		/// <param name="action">The work to run.</param>
		public static void DispatchIfRequired(this IElementHandler? handler, Action action)
		{
			ArgumentNullException.ThrowIfNull(action);

			var dispatcher = handler.GetDispatcher();

			if (dispatcher is null || !dispatcher.IsDispatchRequired)
			{
				action();
				return;
			}

			dispatcher.Dispatch(action);
		}

		/// <summary>
		/// Runs <paramref name="action"/> on the UI thread and completes after it has run.
		/// </summary>
		/// <remarks>
		/// Awaiting the callback is required when validity is checked immediately before a commit.
		/// A fire-and-forget dispatch leaves a gap in which newer state can supersede queued work.
		/// </remarks>
		public static Task DispatchIfRequiredAsync(this IElementHandler? handler, Action action)
		{
			ArgumentNullException.ThrowIfNull(action);

			var dispatcher = handler.GetDispatcher();

			if (dispatcher is null || !dispatcher.IsDispatchRequired)
			{
				action();
				return Task.CompletedTask;
			}

			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var accepted = dispatcher.Dispatch(() =>
			{
				try
				{
					action();
					completion.TrySetResult();
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
			});

			if (!accepted)
			{
				completion.TrySetException(new InvalidOperationException(
					"The dispatcher rejected a UI-thread commit."));
			}

			return completion.Task;
		}

		/// <summary>
		/// The dispatcher for this handler, or <see langword="null"/> if none is available.
		/// </summary>
		/// <remarks>
		/// A handler that has been disconnected, or was never given a context, has no dispatcher.
		/// That is not an error: the caller runs inline, which is correct because there is no
		/// longer a live view to marshal to.
		/// </remarks>
		public static IDispatcher? GetDispatcher(this IElementHandler? handler)
		{
			if (handler?.MauiContext?.Services?.GetService(typeof(IDispatcher)) is IDispatcher fromServices)
				return fromServices;

			return (handler?.VirtualView as IElement)?.Handler?.MauiContext?.Services?.GetService(typeof(IDispatcher)) as IDispatcher;
		}

	}
}
