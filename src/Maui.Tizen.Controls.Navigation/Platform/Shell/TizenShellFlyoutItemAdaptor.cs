using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Shell flyout items to the Tizen CollectionView adaptor model.
	/// </summary>
	public class TizenShellFlyoutItemAdaptor : TizenItemTemplateAdaptor
	{
		TizenItemAppearance? _itemAppearance;
		IMenuItemController? _headerMenu;
		IMenuItemController? _footerMenu;

		public TizenShellFlyoutItemAdaptor(Shell shell, IEnumerable items) :
			base(shell, items, GetFlyoutItemTemplate())
		{
			Shell = shell;
		}

		public Shell Shell { get; }

		public IMenuItemController? HeaderMenu
		{
			get => _headerMenu;
			set => _headerMenu = value;
		}

		public IMenuItemController? FooterMenu
		{
			get => _footerMenu;
			set => _footerMenu = value;
		}

		public TizenItemAppearance? ItemAppearance
		{
			get => _itemAppearance;
			set => _itemAppearance = value;
		}

		protected override bool IsSelectable => true;

		protected override View? CreateHeaderView()
		{
			var controller = Shell as IShellController;
			var header = controller.FlyoutHeader;

			if (header != null)
			{
				DataTemplate? template = header is BindableObject headerItem
					? ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(Shell, headerItem)
					: null;

				View? view = null;
				if (template != null)
				{
					// The resolver may return a selector; resolving it is the caller's job, matching
					// upstream's documented usage pattern.
					view = (View)template.SelectDataTemplate(header, Shell).CreateContent();
					view.BindingContext = header;
				}
				else if (header is View vw)
				{
					view = vw;
				}
				return view;
			}
			return null;
		}

		protected override View? CreateFooterView()
		{
			var controller = Shell as IShellController;
			var footer = controller.FlyoutFooter;

			if (footer != null)
			{
				if (footer is View view)
				{
					return view;
				}
			}
			return null;
		}

		public override NView CreateNativeView(int index)
		{
			var item = this[index];
			if (item is BindableObject bo)
			{
				// The raw item, never a pre-resolved template owner - see
				// ShellFlyoutTemplateResolution for why that distinction matters.
				DataTemplate? template = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(Shell, bo);

				View? view;
				if (template != null)
				{
					// Selector resolution belongs to the caller. The item is both the selector input
					// and the eventual binding context, matching upstream's documented pattern.
					view = (View)template.SelectDataTemplate(item, Shell).CreateContent();
				}
				else
				{
					view = TizenShellFlyoutItemView.GetFlyoutItemView(item, MauiContext, _itemAppearance);
				}

				view.Parent = Shell;
				view.BindingContext = item;

				if (_itemAppearance != null)
				{
					// Use explicit source so binding works even when BindingContext is the flyout item
					view.SetBinding(View.BackgroundColorProperty, static (TizenItemAppearance app) => app.BackgroundColor, source: _itemAppearance);
				}

				var native = view.ToPlatform(MauiContext);

				// Register native-to-MAUI mapping for selection state tracking
				RegisterNativeView(native, view);
				ItemSelectionState.TrackEnabledState(view);

				return native;
			}

			var fallbackView = TizenShellFlyoutItemView.GetFlyoutItemView(item!, MauiContext, _itemAppearance);
			var fallbackNative = fallbackView.ToPlatform(MauiContext);

			// Register native-to-MAUI mapping for selection state tracking
			RegisterNativeView(fallbackNative, fallbackView);
			ItemSelectionState.TrackEnabledState(fallbackView);

			return fallbackNative;
		}

		public override void UpdateViewState(NView view, ViewHolderState state)
		{
			base.UpdateViewState(view, state);
			if (GetRegisteredView(view) is { } formsView)
			{
				switch (state)
				{
					case ViewHolderState.Focused:
						ItemSelectionState.SetItemFocused(formsView, true);
						break;
					case ViewHolderState.Normal:
						ItemSelectionState.Reset(formsView);
						break;
					case ViewHolderState.Selected:
						ItemSelectionState.SetItemSelectedAndUnfocused(formsView, true);
						break;
				}
			}
		}

		public override void RemoveNativeView(NView native)
		{
			// Unregister rather than just look up: leaving the entry behind keeps the view alive and
			// lets a recycled native view resolve to a MAUI view whose handler is already disposed.
			if (UnregisterNativeView(native) is { } view)
			{
				if (view.Handler is ITizenPlatformViewHandler handler)
				{
					handler.Dispose();
					view.Handler = null;
				}
			}

			base.RemoveNativeView(native);
		}


		static DataTemplate GetFlyoutItemTemplate()
		{
			return new DataTemplate(() =>
			{
				return new Controls.StackLayout();
			});
		}
	}
}
