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
				DataTemplate? template = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(Shell, header as BindableObject);

				View? view = null;
				if (template != null)
				{
					view = (View)template.CreateContent();
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
					view = (View)template.CreateContent();
				}
				else
				{
					view = TizenShellFlyoutItemView.GetFlyoutItemView(item, MauiContext);
				}

				view.Parent = Shell;
				view.BindingContext = item;

				if (_itemAppearance != null)
				{
					view.BindingContext = _itemAppearance;
					view.SetBinding(View.BackgroundColorProperty, static (TizenItemAppearance app) => app.BackgroundColor);
					view.BindingContext = item;
				}

				return view.ToPlatform(MauiContext);
			}

			return TizenShellFlyoutItemView.GetFlyoutItemView(item!, MauiContext).ToPlatform(MauiContext);
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
