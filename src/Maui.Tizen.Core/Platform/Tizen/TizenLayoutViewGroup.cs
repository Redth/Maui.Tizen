using System;
using Microsoft.Maui;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using Rect = Microsoft.Maui.Graphics.Rect;
using Size = Microsoft.Maui.Graphics.Size;
using TSize = Tizen.UIExtensions.Common.Size;
using TTouch = Tizen.NUI.Touch;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// NUI container that hosts the platform views of an <see cref="ILayout"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Platform.LayoutViewGroup</c> in dotnet/maui. Behaviour is
	/// preserved verbatim; only the owning type/namespace changed.
	/// </remarks>
	public class TizenLayoutViewGroup : ViewGroup, IMeasurable
	{
		readonly IView? _virtualView;
		Size _measureCache;
		bool _needMeasureUpdate;

		/// <summary>Initializes a new instance of the <see cref="TizenLayoutViewGroup"/> class.</summary>
		/// <param name="virtualView">The cross-platform view this group renders.</param>
		public TizenLayoutViewGroup(IView? virtualView)
		{
			_virtualView = virtualView;
			LayoutUpdated += OnLayoutUpdated;
		}

		internal int IsLayoutUpdating { get; set; }

		/// <summary>Gets the cross-platform view this group renders.</summary>
		public IView? VirtualView => _virtualView;

		/// <summary>Gets or sets the cross-platform measure callback.</summary>
		public Func<double, double, Size>? CrossPlatformMeasure { get; set; }

		/// <summary>Gets or sets the cross-platform arrange callback.</summary>
		public Func<Rect, Size>? CrossPlatformArrange { get; set; }

		/// <summary>Gets or sets a value indicating whether input passes through this group.</summary>
		public bool InputTransparent { get; set; }

		/// <summary>Flags the group as needing a new measure pass.</summary>
		public void SetNeedMeasureUpdate()
		{
			_needMeasureUpdate = true;
			MarkChanged();

			// Suppressed while a layout pass is running; the outermost pass replays it on the way
			// out - see OnLayoutUpdated - so the invalidation is not lost.
			if (IsLayoutUpdating == 0)
			{
				Layout.RequestLayout();
			}
		}

		/// <summary>Clears the pending measure flag.</summary>
		public void ClearNeedMeasureUpdate() => _needMeasureUpdate = false;

		/// <inheritdoc />
		public TSize Measure(double availableWidth, double availableHeight) =>
			InvokeCrossPlatformMeasure(availableWidth.ToScaledDP(), availableHeight.ToScaledDP()).ToPixel();

		/// <inheritdoc />
		protected override bool HitTest(TTouch touch) => !InputTransparent;

		/// <summary>Runs the cross-platform measure pass and caches the result.</summary>
		/// <param name="availableWidth">Available width, in device-independent units.</param>
		/// <param name="availableHeight">Available height, in device-independent units.</param>
		/// <returns>The measured size.</returns>
		public Size InvokeCrossPlatformMeasure(double availableWidth, double availableHeight)
		{
			if (CrossPlatformMeasure == null)
				return Microsoft.Maui.Graphics.Size.Zero;

			var measured = CrossPlatformMeasure(availableWidth, availableHeight);
			if (measured != _measureCache && _virtualView?.Parent is IView parentView)
			{
				parentView?.InvalidateMeasure();
			}

			_measureCache = measured;
			ClearNeedMeasureUpdate();
			return measured;
		}

		void OnLayoutUpdated(object? sender, LayoutEventArgs e)
		{
			IsLayoutUpdating++;
			var platformGeometry = this.GetBounds().ToDP();

			if (_needMeasureUpdate || _measureCache != platformGeometry.Size)
			{
				InvokeCrossPlatformMeasure(platformGeometry.Width, platformGeometry.Height);
			}

			if (platformGeometry.Width > 0 && platformGeometry.Height > 0)
			{
				platformGeometry.X = 0;
				platformGeometry.Y = 0;
				CrossPlatformArrange?.Invoke(platformGeometry);
			}

			IsLayoutUpdating--;

			// Replay a request suppressed during the pass; see ContentViewGroup for why.
			if (TizenPropertyResolvers.ShouldScheduleLayout(IsLayoutUpdating, _needMeasureUpdate))
				Layout?.RequestLayout();
		}
	}
}
