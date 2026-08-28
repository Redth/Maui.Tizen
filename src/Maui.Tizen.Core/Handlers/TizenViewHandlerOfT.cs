using System;
using System.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
#if TIZEN
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using TSize = Tizen.UIExtensions.Common.Size;
#endif
// Tizen.UIExtensions.Common also declares Size/Rect, so bind these names explicitly.
using Rect = Microsoft.Maui.Graphics.Rect;
using Size = Microsoft.Maui.Graphics.Size;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Base class for every Tizen view handler in this backend.
	/// </summary>
	/// <typeparam name="TVirtualView">The cross-platform view type.</typeparam>
	/// <typeparam name="TPlatformView">The native Tizen view type.</typeparam>
	/// <remarks>
	/// <para>
	/// This type derives from the <em>public</em> generic
	/// <see cref="ViewHandler{TVirtualView, TPlatformView}"/> shipped in <c>Microsoft.Maui.Core</c>.
	/// It deliberately does <b>not</b> copy MAUI's partial classes and does not declare a competing
	/// <c>Microsoft.Maui.Handlers.ViewHandler</c>, so there is no CS0433 risk for consumers that
	/// reference both assemblies.
	/// </para>
	/// <para>
	/// Because <c>TPlatformView</c> is constrained to <c>TizenNativeView</c>, MAUI's own constraint
	/// (<c>where TPlatformView : System.Object</c> on a non-platform TFM, or
	/// <c>where TPlatformView : Tizen.NUI.BaseComponents.View</c> on a <c>-tizen</c> TFM) is always
	/// satisfied on both target frameworks.
	/// </para>
	/// </remarks>
	public abstract class TizenViewHandler<TVirtualView, TPlatformView> :
		ViewHandler<TVirtualView, TPlatformView>,
		ITizenPlatformViewHandler
		where TVirtualView : class, IView
		where TPlatformView : TizenNativeView
	{
		const int NotDisposed = 0;
		const int Disposing = 1;
		const int Disposed = 2;

		int _disposeState;

		/// <summary>Initializes a new instance of the handler.</summary>
		/// <param name="mapper">The property mapper.</param>
		/// <param name="commandMapper">The command mapper.</param>
		protected TizenViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper)
		{
		}

		/// <summary>Finalizes the handler.</summary>
		~TizenViewHandler() => Dispose(disposing: false);

		TizenNativeView? ITizenPlatformViewHandler.PlatformView =>
			((IElementHandler)this).PlatformView as TizenNativeView;

		TizenNativeView? ITizenPlatformViewHandler.ContainerView =>
			((IViewHandler)this).ContainerView as TizenNativeView;

		/// <summary>
		/// Gets a value indicating whether this handler needs a container view.
		/// </summary>
		/// <remarks>
		/// Always <see langword="false"/>. MAUI declares
		/// <c>ViewHandler.ContainerView { get; private protected set; }</c>, so an out-of-repo
		/// backend has no way to publish a container it constructs in
		/// <see cref="SetupContainer"/>. Until MAUI exposes a settable container hook, this backend
		/// renders background, clip and shadow directly onto the platform view instead of wrapping
		/// it. See docs/net11-status.md ("Required public MAUI API gaps").
		/// </remarks>
		public override bool NeedsContainer => false;

		/// <inheritdoc />
		/// <remarks>
		/// Intentionally empty - see <see cref="NeedsContainer"/>. MAUI only calls this when
		/// <c>HasContainer</c> flips to <see langword="true"/>, which cannot happen while
		/// <see cref="NeedsContainer"/> is <see langword="false"/>.
		/// </remarks>
		protected override void SetupContainer()
		{
		}

		/// <inheritdoc />
		/// <remarks>Intentionally empty - see <see cref="NeedsContainer"/>.</remarks>
		protected override void RemoveContainer()
		{
		}

		/// <inheritdoc />
		public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
		{
#if TIZEN
			var platformView = ((IElementHandler)this).ToPlatformView();
			var virtualView = ((IViewHandler)this).VirtualView;

			if (platformView is null || virtualView is null)
			{
				return virtualView is null || double.IsNaN(virtualView.Width) || double.IsNaN(virtualView.Height)
					? Size.Zero
					: new Size(virtualView.Width, virtualView.Height);
			}

			var availableWidthAsInt = widthConstraint.ToScaledPixel();
			var availableHeightAsInt = heightConstraint.ToScaledPixel();

			var availableWidth = (availableWidthAsInt < 0 || availableWidthAsInt == int.MaxValue)
				? double.PositiveInfinity
				: availableWidthAsInt;
			var availableHeight = (availableHeightAsInt < 0 || availableHeightAsInt == int.MaxValue)
				? double.PositiveInfinity
				: availableHeightAsInt;

			double? explicitWidth = virtualView.Width >= 0 ? virtualView.Width : null;
			double? explicitHeight = virtualView.Height >= 0 ? virtualView.Height : null;

			var measured = Measure(availableWidth, availableHeight);

			return new Size(explicitWidth ?? measured.Width, explicitHeight ?? measured.Height);
#else
			var virtualView = ((IViewHandler)this).VirtualView;
			if (virtualView is null || double.IsNaN(virtualView.Width) || double.IsNaN(virtualView.Height))
				return Size.Zero;

			return new Size(virtualView.Width, virtualView.Height);
#endif
		}

		/// <summary>
		/// Measures the platform view against the supplied pixel constraints.
		/// </summary>
		/// <param name="availableWidth">Available width, in scaled pixels.</param>
		/// <param name="availableHeight">Available height, in scaled pixels.</param>
		/// <returns>The measured size, in device-independent units.</returns>
		protected virtual Size Measure(double availableWidth, double availableHeight)
		{
#if TIZEN
			if (PlatformView is IMeasurable measurable)
				return measurable.Measure(availableWidth, availableHeight).ToDP();

			var width = Math.Max(PlatformView.MinimumSize.Width, PlatformView.NaturalSize.Width);
			var height = Math.Max(PlatformView.MinimumSize.Height, PlatformView.NaturalSize.Height);
			return new TSize(width, height).ToDP();
#else
			_ = availableWidth;
			_ = availableHeight;
			return Size.Zero;
#endif
		}

		/// <inheritdoc />
		public override void PlatformArrange(Rect frame)
		{
#if TIZEN
			var platformView = ((IElementHandler)this).ToPlatformView();
			if (platformView is null)
				return;

			// Negative sizes are the pre-layout sentinel; nothing is actually being laid out yet.
			if (frame.Width < 0 || frame.Height < 0)
				return;

			var bounds = frame.ToPixel();
			if (platformView.Layout is not null)
			{
				platformView.Layout.MeasuredWidth = new global::Tizen.NUI.MeasuredSize(
					new global::Tizen.NUI.LayoutLength((float)bounds.Width),
					global::Tizen.NUI.MeasuredSize.StateType.MeasuredSizeOK);
				platformView.Layout.MeasuredHeight = new global::Tizen.NUI.MeasuredSize(
					new global::Tizen.NUI.LayoutLength((float)bounds.Height),
					global::Tizen.NUI.MeasuredSize.StateType.MeasuredSizeOK);
			}

			platformView.UpdateBounds(bounds);

			((IViewHandler)this).Invoke(nameof(IView.Frame), frame);
#else
			_ = frame;
#endif
		}

		/// <inheritdoc />
		protected override void ConnectHandler(TPlatformView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.FocusGained += OnFocusGained;
			platformView.FocusLost += OnFocusLost;
#endif
		}

		/// <inheritdoc />
		protected override void DisconnectHandler(TPlatformView platformView)
		{
#if TIZEN
			TizenCleanup.Run(
				() => platformView.FocusGained -= OnFocusGained,
				() => platformView.FocusLost -= OnFocusLost,
				() => base.DisconnectHandler(platformView));
#else
			base.DisconnectHandler(platformView);
#endif
		}

		/// <summary>Called after the platform view gains focus.</summary>
		protected virtual void OnFocused()
		{
		}

		/// <summary>Called after the platform view loses focus.</summary>
		protected virtual void OnUnfocused()
		{
		}

		void OnFocusGained(object? sender, EventArgs e)
		{
			if (((IViewHandler)this).VirtualView is IView view)
				view.IsFocused = true;

			OnFocused();
		}

		void OnFocusLost(object? sender, EventArgs e)
		{
			if (((IViewHandler)this).VirtualView is IView view)
				view.IsFocused = false;

			OnUnfocused();
		}

		/// <inheritdoc />
		public void Dispose()
		{
			try
			{
				Dispose(disposing: true);
			}
			finally
			{
				GC.SuppressFinalize(this);
			}
		}

		/// <summary>Releases resources held by the handler.</summary>
		/// <param name="disposing">Whether managed resources should be released.</param>
		protected virtual void Dispose(bool disposing)
		{
			if (Interlocked.CompareExchange(ref _disposeState, Disposing, NotDisposed) != NotDisposed)
				return;

			try
			{
				if (disposing)
				{
					var platformView = ((IElementHandler)this).PlatformView as IDisposable;

					TizenCleanup.Run(
						() => ((IElementHandler)this).DisconnectHandler(),
						() => platformView?.Dispose());
				}
			}
			finally
			{
				Volatile.Write(ref _disposeState, Disposed);
			}
		}
	}
}
