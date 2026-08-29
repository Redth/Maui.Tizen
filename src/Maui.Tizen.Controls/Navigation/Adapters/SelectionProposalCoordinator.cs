using System;

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
				return true;

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
}
