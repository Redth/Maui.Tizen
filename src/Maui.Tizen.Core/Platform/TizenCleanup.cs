// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Runs every ownership cleanup step before preserving or aggregating failures.
	/// </summary>
	internal static class TizenCleanup
	{
		public static void Run(params Action[] actions)
		{
			ArgumentNullException.ThrowIfNull(actions);

			var errors = new List<Exception>();

			foreach (var action in actions)
			{
				if (action is null)
					continue;

				try
				{
					action();
				}
				catch (Exception exception)
				{
					Add(errors, exception);
				}
			}

			ThrowIfAny(errors);
		}

		public static void Add(ICollection<Exception> errors, Exception exception)
		{
			ArgumentNullException.ThrowIfNull(errors);
			ArgumentNullException.ThrowIfNull(exception);

			if (exception is AggregateException aggregate)
			{
				foreach (var inner in aggregate.Flatten().InnerExceptions)
					errors.Add(inner);
			}
			else
			{
				errors.Add(exception);
			}
		}

		public static void ThrowIfAny(IReadOnlyList<Exception> errors)
		{
			ArgumentNullException.ThrowIfNull(errors);

			if (errors.Count == 0)
				return;

			if (errors.Count == 1)
				ExceptionDispatchInfo.Capture(errors[0]).Throw();

			throw new AggregateException(errors);
		}
	}
}
