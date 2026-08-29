using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>Replaces an owned resource in detach, unsubscribe, dispose, subscribe, attach order.</summary>
	internal sealed class OwnedReplacementCoordinator<T>
		where T : class
	{
		public T? Current { get; private set; }

		public void Replace(
			T? replacement,
			Action detachNative,
			Action<T> unsubscribe,
			Action<T> dispose,
			Action<T> subscribe,
			Action<T?> attachNative)
		{
			ArgumentNullException.ThrowIfNull(detachNative);
			ArgumentNullException.ThrowIfNull(unsubscribe);
			ArgumentNullException.ThrowIfNull(dispose);
			ArgumentNullException.ThrowIfNull(subscribe);
			ArgumentNullException.ThrowIfNull(attachNative);

			var outgoing = Current;
			List<Exception>? errors = null;

			Capture(detachNative, ref errors);
			if (outgoing is not null)
			{
				Capture(() => unsubscribe(outgoing), ref errors);
				if (!ReferenceEquals(outgoing, replacement))
					Capture(() => dispose(outgoing), ref errors);
			}

			Current = replacement;
			if (replacement is not null)
				Capture(() => subscribe(replacement), ref errors);
			Capture(() => attachNative(replacement), ref errors);

			if (errors is { Count: > 0 })
				throw new AggregateException(errors);
		}

		static void Capture(Action action, ref List<Exception>? errors)
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
	}
}
