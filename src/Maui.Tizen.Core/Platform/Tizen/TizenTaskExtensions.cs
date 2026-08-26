// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Task helpers for the Tizen handlers.
	/// </summary>
	/// <remarks>
	/// MAUI has an equivalent <c>FireAndForget</c>, but it is internal to
	/// <c>Microsoft.Maui.Core</c> and therefore unavailable to an out-of-tree backend.
	/// </remarks>
	public static class TizenTaskExtensions
	{
		/// <summary>
		/// Observes <paramref name="task"/> without awaiting it, reporting any failure.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Property mappers are synchronous but some of them start asynchronous work (image
		/// loading, chiefly). Dropping the task on the floor would leave the exception
		/// unobserved, which on some runtime configurations tears down the process at an
		/// unrelated later point.
		/// </para>
		/// <para>
		/// Cancellation is not an error here: a load is routinely cancelled because the source
		/// changed again before it finished.
		/// </para>
		/// </remarks>
		public static void FireAndForget(this Task task, IElementHandler? handler = null)
		{
			ArgumentNullException.ThrowIfNull(task);

			_ = Awaited(task, handler);

			static async Task Awaited(Task task, IElementHandler? handler)
			{
				try
				{
					await task.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					// Superseded by a newer request.
				}
				catch (Exception ex)
				{
					global::Tizen.UIExtensions.Common.Log.Error(
						$"Unhandled exception in an asynchronous {handler?.GetType().Name ?? "handler"} mapping: {ex}");
				}
			}
		}
	}
}
