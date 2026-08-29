using System;
using System.Collections.Generic;
using System.Linq;

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

	internal sealed class GenerationGuard
	{
		int _generation;

		public int Advance() => ++_generation;

		public int Capture() => _generation;

		public bool RunIfCurrent(int generation, Action action)
		{
			ArgumentNullException.ThrowIfNull(action);
			if (generation != _generation)
				return false;

			action();
			return true;
		}
	}

	internal sealed class RealizedItemOwnership<TItem, TOwner>
		where TItem : class
		where TOwner : class
	{
		readonly Dictionary<TItem, TOwner> _owners = new();

		public void Track(TItem item, TOwner owner) => _owners[item] = owner;

		public void ReleaseRemoved(IEnumerable<TItem> liveItems, Action<TItem, TOwner> release)
		{
			ArgumentNullException.ThrowIfNull(liveItems);
			ArgumentNullException.ThrowIfNull(release);

			var live = liveItems.ToHashSet();
			foreach (var item in _owners.Keys.Where(item => !live.Contains(item)).ToList())
				Release(item, release);
		}

		public void ReleaseAll(Action<TItem, TOwner> release)
		{
			ArgumentNullException.ThrowIfNull(release);
			foreach (var item in _owners.Keys.ToList())
				Release(item, release);
		}

		void Release(TItem item, Action<TItem, TOwner> release)
		{
			if (_owners.Remove(item, out var owner))
				release(item, owner);
		}
	}
}
