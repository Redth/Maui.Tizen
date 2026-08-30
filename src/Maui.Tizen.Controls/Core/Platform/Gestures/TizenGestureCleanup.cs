using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal static class TizenGestureCleanup
	{
		public static void Run(string message, params Action[] cleanup)
		{
			List<Exception>? failures = null;

			foreach (var action in cleanup)
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					(failures ??= new()).Add(ex);
				}
			}

			if (failures is not null)
			{
				throw new AggregateException(message, failures);
			}
		}
	}
}
