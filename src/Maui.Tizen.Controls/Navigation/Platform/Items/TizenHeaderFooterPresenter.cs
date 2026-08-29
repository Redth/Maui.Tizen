using System;
using Microsoft.Maui.Controls;
using Tizen.NUI.BaseComponents;
using TSize = Tizen.UIExtensions.Common.Size;
using XView = Microsoft.Maui.Controls.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>Owns the managed and native lifetime of a structured items header and footer.</summary>
	internal sealed class TizenHeaderFooterPresenter : IDisposable
	{
		readonly StructuredItemsView _itemsView;
		readonly Func<IMauiContext> _getContext;
		readonly Action _measureInvalidated;
		XView? _header;
		XView? _footer;

		public TizenHeaderFooterPresenter(
			StructuredItemsView itemsView,
			Func<IMauiContext> getContext,
			Action measureInvalidated)
		{
			_itemsView = itemsView;
			_getContext = getContext;
			_measureInvalidated = measureInvalidated;
		}

		public XView? Header => _header;

		public XView? Footer => _footer;

		public global::Tizen.NUI.BaseComponents.View? GetHeaderView() =>
			Create(ref _header, _itemsView.Header, _itemsView.HeaderTemplate);

		public global::Tizen.NUI.BaseComponents.View? GetFooterView() =>
			Create(ref _footer, _itemsView.Footer, _itemsView.FooterTemplate);

		public TSize MeasureHeader(double width, double height) => Measure(_header, width, height);

		public TSize MeasureFooter(double width, double height) => Measure(_footer, width, height);

		global::Tizen.NUI.BaseComponents.View? Create(
			ref XView? cache,
			object? value,
			DataTemplate? template)
		{
			Release(ref cache);
			if (value is null)
				return null;

			cache = value as XView
				?? template?.CreateContent() as XView
				?? new Label { Text = value.ToString() ?? string.Empty };

			if (value is not XView)
				cache.BindingContext = value;

			cache.Parent = _itemsView;
			cache.MeasureInvalidated += OnMeasureInvalidated;
			return cache.ToPlatformView(_getContext());
		}

		static TSize Measure(XView? view, double width, double height)
		{
			if (view is not IView measurable)
				return new TSize(0, 0);

			return measurable.Measure(width.ToScaledDP(), height.ToScaledDP()).ToPixel();
		}

		void OnMeasureInvalidated(object? sender, EventArgs e) => _measureInvalidated();

		void Release(ref XView? view)
		{
			if (view is null)
				return;

			view.MeasureInvalidated -= OnMeasureInvalidated;
			(view.Handler as IDisposable)?.Dispose();
			view.Handler = null;
			view.Parent = null;
			view = null;
		}

		public void Dispose()
		{
			Release(ref _header);
			Release(ref _footer);
		}
	}
}
