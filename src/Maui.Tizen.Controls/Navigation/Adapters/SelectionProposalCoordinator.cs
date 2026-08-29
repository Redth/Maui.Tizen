using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>Coordinates managed selection pushes with vetoable native proposals.</summary>
	internal sealed class SelectionProposalCoordinator<T>
		where T : class
	{
		readonly BidirectionalUpdateGate _gate = new();
		int? _pendingManagedIndex;

		public bool IsApplyingManaged => _gate.IsApplyingManaged;

		public void Synchronize(
			T? current,
			Func<T, int> getIndex,
			Action clear,
			Action<int> select)
		{
			ArgumentNullException.ThrowIfNull(getIndex);
			ArgumentNullException.ThrowIfNull(clear);
			ArgumentNullException.ThrowIfNull(select);

			var index = current is null ? -1 : getIndex(current);
			_pendingManagedIndex = index;

			_gate.ApplyManaged(() =>
			{
				clear();
				if (index >= 0)
					select(index);
			});
		}

		public bool ConsumeManagedEcho(int nativeIndex)
		{
			if (_gate.IsApplyingManaged)
			{
				if (_pendingManagedIndex == nativeIndex)
					_pendingManagedIndex = null;
				return true;
			}

			if (_pendingManagedIndex == nativeIndex)
			{
				_pendingManagedIndex = null;
				return true;
			}

			// A managed replacement clears the prior selection before selecting the new one. If
			// native notifications are deferred, consume that intermediate empty event without
			// forgetting the final index that is still expected.
			if (nativeIndex < 0 && _pendingManagedIndex >= 0)
				return true;

			_pendingManagedIndex = null;
			return false;
		}

		public bool Propose(
			T proposed,
			Func<T, bool> propose,
			Action restore)
		{
			ArgumentNullException.ThrowIfNull(proposed);
			ArgumentNullException.ThrowIfNull(propose);
			ArgumentNullException.ThrowIfNull(restore);

			if (IsApplyingManaged)
				return false;

			var accepted = false;
			_gate.ApplyNative(() => accepted = propose(proposed));

			if (!accepted)
				restore();

			return accepted;
		}
	}

	internal static class HierarchySelectionResolver
	{
		public static T? Resolve<T>(
			IEnumerable<T> generated,
			T? root,
			T? section,
			T? content)
			where T : class
		{
			ArgumentNullException.ThrowIfNull(generated);

			T? resolved = null;
			var rank = -1;
			foreach (var candidate in generated)
			{
				var candidateRank = ReferenceEquals(candidate, content) ? 3
					: ReferenceEquals(candidate, section) ? 2
					: ReferenceEquals(candidate, root) ? 1
					: -1;
				if (candidateRank > rank)
				{
					resolved = candidate;
					rank = candidateRank;
				}
			}

			return resolved;
		}
	}

	internal sealed class AsyncSelectionResynchronizer<TOwner>
		where TOwner : class
	{
		int _generation;

		public void Invalidate() => _generation++;

		public async Task<bool> RunAsync(
			TOwner owner,
			Func<Task> select,
			Func<TOwner, bool> isCurrent,
			Action synchronize)
		{
			ArgumentNullException.ThrowIfNull(owner);
			ArgumentNullException.ThrowIfNull(select);
			ArgumentNullException.ThrowIfNull(isCurrent);
			ArgumentNullException.ThrowIfNull(synchronize);

			var generation = ++_generation;
			var current = false;
			try
			{
				await select().ConfigureAwait(true);
			}
			finally
			{
				current = generation == _generation && isCurrent(owner);
				if (current)
					synchronize();
			}
			return current;
		}
	}

	internal static class FlyoutContentMode
	{
		public static bool UsesGeneratedContent<T>(T? customContent)
			where T : class =>
			customContent is null;
	}

	internal static class FlyoutHeaderOwnership
	{
		public static bool UseScrollingHeader(bool headerOnMenu, bool usesGeneratedContent) =>
			headerOnMenu && usesGeneratedContent;

		public static bool UseFixedHeader(bool headerOnMenu, bool usesGeneratedContent) =>
			!UseScrollingHeader(headerOnMenu, usesGeneratedContent);
	}
}
