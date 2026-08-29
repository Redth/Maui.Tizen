using System;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Prevents a managed-to-native update from echoing back as a native-to-managed update, and
	/// vice versa.
	/// </summary>
	internal sealed class BidirectionalUpdateGate
	{
		int _managedDepth;
		int _nativeDepth;

		public bool IsApplyingManaged => _managedDepth != 0;

		public bool IsApplyingNative => _nativeDepth != 0;

		public bool ApplyManaged(Action action)
		{
			ArgumentNullException.ThrowIfNull(action);

			if (IsApplyingNative)
				return false;

			_managedDepth++;
			try
			{
				action();
				return true;
			}
			finally
			{
				_managedDepth--;
			}
		}

		public bool ApplyNative(Action action)
		{
			ArgumentNullException.ThrowIfNull(action);

			if (IsApplyingManaged)
				return false;

			_nativeDepth++;
			try
			{
				action();
				return true;
			}
			finally
			{
				_nativeDepth--;
			}
		}
	}
}
