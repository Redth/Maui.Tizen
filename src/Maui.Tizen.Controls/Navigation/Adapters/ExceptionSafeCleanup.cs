using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	internal static class ExceptionSafeCleanup
	{
		public static void Run(params Action[] actions)
		{
			List<Exception>? errors = null;
			foreach (var action in actions)
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					(errors ??= new()).Add(ex);
				}
			}

			if (errors is null)
				return;

			if (errors.Count == 1)
				ExceptionDispatchInfo.Capture(errors[0]).Throw();

			throw new AggregateException(errors);
		}
	}
}
