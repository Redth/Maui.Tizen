using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IAppActions"/>.
	/// </summary>
	/// <remarks>
	/// Tizen has no home-screen shortcut / quick action contract comparable to Android app shortcuts,
	/// iOS home screen quick actions, or Windows jump lists. <see cref="IsSupported"/> reports
	/// <see langword="false"/> and the mutating members throw
	/// <see cref="FeatureNotSupportedException"/> instead of silently accepting actions that would
	/// never be surfaced to the user.
	/// </remarks>
	public sealed class TizenAppActions : IAppActions
	{
		const string Reason =
			"Tizen provides no home screen shortcut or quick action API on any device profile " +
			"(mobile, wearable, TV or common).";

		/// <inheritdoc/>
		public bool IsSupported => false;

		/// <inheritdoc/>
		/// <remarks>Never raised: Tizen cannot activate app actions.</remarks>
		public event EventHandler<AppActionEventArgs>? AppActionActivated
		{
			add { }
			remove { }
		}

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<IEnumerable<AppAction>> GetAsync() =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IAppActions)}.{nameof(GetAsync)}", Reason);

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task SetAsync(IEnumerable<AppAction> actions) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IAppActions)}.{nameof(SetAsync)}", Reason);
	}
}
