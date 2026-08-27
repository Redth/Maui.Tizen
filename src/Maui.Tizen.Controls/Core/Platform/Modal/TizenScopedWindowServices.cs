using System;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// A window-scoped <see cref="ITizenNavigationStack"/> whose target is supplied once the
	/// window's native stack exists.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Registration happens at host-build time, but a window's native navigation stack only exists
	/// once the window handler runs. This holder is registered scoped so that dependents can be
	/// resolved from the window scope immediately, and the Tizen window handler fills it in with
	/// <see cref="Attach"/> when the window is created.
	/// </para>
	/// <para>
	/// Calls made before attachment throw <see cref="InvalidOperationException"/> rather than
	/// silently doing nothing, because a modal that reports success without appearing is worse than
	/// a clear failure.
	/// </para>
	/// </remarks>
	public sealed class TizenScopedNavigationStack : ITizenNavigationStack
	{
		ITizenNavigationStack? _target;

		/// <summary>
		/// Gets a value indicating whether a native stack has been attached.
		/// </summary>
		public bool IsAttached => _target is not null;

		/// <summary>Supplies the window's native navigation stack.</summary>
		/// <param name="target">The stack to delegate to.</param>
		public void Attach(ITizenNavigationStack target) =>
			_target = target ?? throw new ArgumentNullException(nameof(target));

		/// <summary>Clears the attached stack.</summary>
		public void Detach() => _target = null;

		/// <inheritdoc/>
		public int Count => _target?.Count ?? 0;

		/// <inheritdoc/>
		public object? Top => _target?.Top;

		/// <inheritdoc/>
		public bool Contains(object platformView) => _target?.Contains(platformView) ?? false;

		/// <inheritdoc/>
		public bool ShownBehindPage
		{
			get => _target?.ShownBehindPage ?? false;
			set
			{
				if (_target is not null)
				{
					_target.ShownBehindPage = value;
				}
			}
		}

		/// <inheritdoc/>
		public object CreatePlaceholder() => Target.CreatePlaceholder();

		/// <inheritdoc/>
		public Task PushAsync(object platformView, bool animated) => Target.PushAsync(platformView, animated);

		/// <inheritdoc/>
		public Task PopAsync(bool animated) => Target.PopAsync(animated);

		/// <inheritdoc/>
		public bool Remove(object platformView) => Target.Remove(platformView);

		ITizenNavigationStack Target =>
			_target ?? throw new InvalidOperationException(
				"No native navigation stack has been attached to this window scope. The Tizen window handler " +
				"is expected to call TizenNuiHostingExtensions.AttachTizenWindow when the window is created.");
	}

	/// <summary>
	/// A window-scoped <see cref="ITizenWindowBackButton"/> whose target is supplied once the
	/// window exists.
	/// </summary>
	/// <remarks>
	/// Unlike the navigation stack, a missing back-button target is not fatal: the app still runs,
	/// the hardware back button just falls through to the platform default. Calls made before
	/// attachment are therefore remembered and replayed on <see cref="Attach"/>.
	/// </remarks>
	public sealed class TizenScopedWindowBackButton : ITizenWindowBackButton
	{
		ITizenWindowBackButton? _target;
		Func<bool>? _pendingHandler;
		IDisposable? _targetRegistration;

		/// <summary>
		/// Gets a value indicating whether a native window has been attached.
		/// </summary>
		public bool IsAttached => _target is not null;

		/// <summary>Supplies the window's back-button sink.</summary>
		/// <param name="target">The sink to delegate to.</param>
		public void Attach(ITizenWindowBackButton target)
		{
			_target = target ?? throw new ArgumentNullException(nameof(target));
			_targetRegistration?.Dispose();
			_targetRegistration = null;

			// The modal navigation platform installs its handler on PageAttached, which can happen
			// before the window handler attaches the native window.
			if (_pendingHandler is not null)
			{
				_targetRegistration = _target.RegisterBackButtonPressedHandler(_pendingHandler);
			}
		}

		/// <summary>Clears the attached window.</summary>
		public void Detach()
		{
			_targetRegistration?.Dispose();
			_targetRegistration = null;
			_target = null;
		}

		/// <inheritdoc/>
		public IDisposable RegisterBackButtonPressedHandler(Func<bool> handler)
		{
			ArgumentNullException.ThrowIfNull(handler);

			_targetRegistration?.Dispose();
			_pendingHandler = handler;
			_targetRegistration = _target?.RegisterBackButtonPressedHandler(handler);

			return new Registration(this, handler);
		}

		sealed class Registration : IDisposable
		{
			TizenScopedWindowBackButton? _owner;
			readonly Func<bool> _handler;

			public Registration(TizenScopedWindowBackButton owner, Func<bool> handler)
			{
				_owner = owner;
				_handler = handler;
			}

			public void Dispose()
			{
				var owner = _owner;
				_owner = null;

				if (owner is null || !ReferenceEquals(owner._pendingHandler, _handler))
				{
					return;
				}

				owner._pendingHandler = null;
				owner._targetRegistration?.Dispose();
				owner._targetRegistration = null;
			}
		}
	}
}
