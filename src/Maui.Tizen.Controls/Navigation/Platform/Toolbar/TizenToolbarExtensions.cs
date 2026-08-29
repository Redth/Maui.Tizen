using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using GColor = Microsoft.Maui.Graphics.Color;
using MaterialIcons = Tizen.UIExtensions.Common.GraphicsView.MaterialIcons;
using NView = Tizen.NUI.BaseComponents.View;
using TButton = Tizen.UIExtensions.NUI.Button;
using TColor = Tizen.UIExtensions.Common.Color;
using TDeviceInfo = Tizen.UIExtensions.Common.DeviceInfo;
using TMaterialIconButton = Tizen.UIExtensions.NUI.GraphicsView.MaterialIconButton;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Tizen toolbar update helpers.
	/// </summary>
	/// <remarks>
	/// Ported from the in-tree <c>Microsoft.Maui.Controls.Platform.ToolbarExtensions</c>, which was
	/// <c>internal</c>. It is public here because an out-of-tree backend has no way to share
	/// internals with the handler, and because the shell view legitimately needs to recolour the
	/// toolbar when shell appearance changes.
	/// </remarks>
	internal static class TizenToolbarExtensions
	{
		const double ToolbarItemTextSize = 16d;
		const double ToolbarItemMaxWidth = 80d;

		static readonly TColor DefaultBackgroundColor = TColor.FromHex("#2196f3");

		// NOTE: UpdateTitle is deliberately NOT redefined here. Setting the toolbar title is a
		// Core-level concern and is already provided by Microsoft.Maui.Platform.ToolbarExtensions
		// (Maui.Tizen.Core). Redefining it produced a genuine CS0121 ambiguity, which is exactly the
		// duplicate-surface problem this migration is meant to avoid.

		public static void UpdateIsVisible(this TizenToolbarView platformToolbar, IToolbar toolbar)
		{
			if (toolbar.IsVisible)
			{
				platformToolbar.Expand();
			}
			else
			{
				platformToolbar.Collapse();
			}
		}

		/// <summary>
		/// Renders the toolbar's leading icon, preferring the back button over the drawer toggle.
		/// </summary>
		/// <param name="drawerToggleVisible">
		/// Whether a drawer toggle is available. Read-only capability, supplied by the caller; see
		/// <see cref="ToolbarDrawerToggle"/>.
		/// </param>
		/// <remarks>
		/// BACK-PRECEDENCE, NOT MUTUAL EXCLUSIVITY. <paramref name="drawerToggleVisible"/> may be
		/// true at the same time as <see cref="IToolbar.BackButtonVisible"/> - a shell in flyout mode
		/// still has a drawer while a pushed page shows a back button. Only one icon fits, so the
		/// back button wins here; the capability itself is never forced false.
		/// </remarks>
		public static void UpdateBackButton(this TizenToolbarView platformToolbar, Toolbar toolbar, bool drawerToggleVisible)
		{
			// Taken up front so that any title-icon load already in flight is superseded: it must not
			// be allowed to land after the navigation icon decided below.
			_ = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);

			switch (TizenToolbarNavigationSlot.GetNavigationIconKind(toolbar, drawerToggleVisible))
			{
				case TizenNavigationIconKind.BackButton:
					platformToolbar.Icon = CreateNavigationIconButton(platformToolbar, toolbar.IconColor, MaterialIcons.ArrowBack);
					break;

				case TizenNavigationIconKind.DrawerToggle:
					platformToolbar.Icon = CreateNavigationIconButton(platformToolbar, toolbar.IconColor, MaterialIcons.Menu);
					break;

				default:
					// Nothing owns the slot, so the handler-owned typed image loader may claim it.
					// Do not clear a title icon that is still loading.
					if (toolbar.TitleIcon is null)
					{
						platformToolbar.Icon = null;
					}

					break;
			}
		}

		public static void UpdateBarBackgroundColor(this TizenToolbarView platformToolbar, Toolbar toolbar)
		{
			// TODO: gradient and image brushes are not yet mapped; tracked as a Partial entry in
			// Parity/MapperParity.json rather than silently rendering the default colour.
			if (toolbar.BarBackground is SolidColorBrush solidColor)
			{
				platformToolbar.BackgroundColor = solidColor.Color.ToTizen().ToNative();
			}
			else
			{
				platformToolbar.UpdateBarBackgroundColor(GColor.FromRgba(0, 0, 0, 0));
			}

			if (platformToolbar.Icon is TMaterialIconButton button)
			{
				button.Color = toolbar.IconColor.IsNotDefault()
					? toolbar.IconColor.ToTizen()
					: platformToolbar.GetAccentColor();
			}
		}

		public static void UpdateBarBackgroundColor(this TizenToolbarView platformToolbar, GColor? color)
			=> platformToolbar.BackgroundColor = color.IsNotDefault()
				? color!.ToTizen().ToNative()
				: DefaultBackgroundColor.ToNative();

		public static void UpdateBarTextColor(this TizenToolbarView platformToolbar, Toolbar toolbar)
			=> platformToolbar.UpdateBarTextColor(toolbar.BarTextColor);

		public static void UpdateBarTextColor(this TizenToolbarView platformToolbar, GColor? color)
			=> platformToolbar.Label.TextColor = color.IsNotDefault()
				? color!.ToTizen()
				: platformToolbar.GetAccentColor();

		public static void UpdateBarIconColor(this TizenToolbarView platformToolbar, GColor? color)
		{
			if (platformToolbar.Icon is TMaterialIconButton button)
			{
				button.Color = color.IsNotDefault() ? color!.ToTizen() : platformToolbar.GetAccentColor();
			}
		}

		/// <summary>
		/// Rebuilds the toolbar's action area from <paramref name="toolbar"/>'s toolbar items.
		/// </summary>
		/// <remarks>
		/// Primary items become inline buttons. Secondary items collapse behind an overflow button
		/// which delegates to <see cref="IToolbarSecondaryActionPresenter"/>; when no presenter is
		/// registered the overflow button is omitted entirely rather than rendering a button that
		/// does nothing when tapped.
		/// </remarks>
		public static void UpdateMenuItems(
			this TizenToolbarView platformToolbar,
			Toolbar toolbar,
			IMauiContext? mauiContext,
			Action<ImageSource, TButton>? loadIcon = null)
		{
			platformToolbar.Actions.Clear();

			IReadOnlyList<ToolbarItem>? toolbarItems = toolbar.ToolbarItems?.ToList();

			if (toolbarItems is null || toolbarItems.Count == 0)
			{
				return;
			}

			foreach (NView action in GetPrimaryActionButtons(platformToolbar, toolbarItems, loadIcon))
			{
				platformToolbar.Actions.Add(action);
			}

			List<ToolbarItem> secondaryActions = toolbarItems
				.Where(static i => i.Order == ToolbarItemOrder.Secondary)
				.OrderBy(static i => i.Priority)
				.ToList();

			if (secondaryActions.Count == 0)
			{
				return;
			}

			IToolbarSecondaryActionPresenter? presenter =
				mauiContext?.Services?.GetService<IToolbarSecondaryActionPresenter>();

			if (presenter is null)
			{
				return;
			}

			TMaterialIconButton more = CreateIconButton(platformToolbar, toolbar.IconColor, MaterialIcons.MoreVert);
			more.IsEnabled = secondaryActions.Any(MenuItemActivation.CanActivate);

			more.Clicked += async (_, _) =>
			{
				List<string?> labels = secondaryActions.Select(static i => (string?)i.Text).ToList();
				int selected = await presenter.PresentAsync(labels, "Cancel").ConfigureAwait(true);

				if (selected >= 0 && selected < secondaryActions.Count)
				{
					MenuItemActivation.Activate(secondaryActions[selected]);
				}
			};

			platformToolbar.Actions.Add(more);
		}

		internal static TColor GetAccentColor(this TizenToolbarView platformToolbar)
		{
			float grayscale =
				(platformToolbar.BackgroundColor.R + platformToolbar.BackgroundColor.G + platformToolbar.BackgroundColor.B) / 3.0f;

			return grayscale > 0.6 ? TColor.Black : TColor.White;
		}

		static IEnumerable<NView> GetPrimaryActionButtons(
			TizenToolbarView platformToolbar,
			IEnumerable<ToolbarItem> toolbarItems,
			Action<ImageSource, TButton>? loadIcon)
			=> toolbarItems
				.Where(static i => i.Order <= ToolbarItemOrder.Primary)
				.OrderBy(static i => i.Priority)
				.Select(i => CreateToolbarButton(platformToolbar, i, loadIcon));

		static TMaterialIconButton CreateIconButton(TizenToolbarView platformToolbar, GColor? iconColor, MaterialIcons icon)
		{
			return new TMaterialIconButton
			{
				Icon = icon,
				Color = iconColor.IsNotDefault() ? iconColor!.ToTizen() : platformToolbar.GetAccentColor(),
			};
		}

		static TMaterialIconButton CreateNavigationIconButton(
			TizenToolbarView platformToolbar,
			GColor? iconColor,
			MaterialIcons icon)
		{
			var button = CreateIconButton(platformToolbar, iconColor, icon);
			button.Clicked += (_, _) => platformToolbar.SendIconPressed();
			return button;
		}

		static NView CreateToolbarButton(
			TizenToolbarView platformToolbar,
			ToolbarItem item,
			Action<ImageSource, TButton>? loadIcon)
		{
			TColor accentColor = platformToolbar.GetAccentColor();

			TButton button = new()
			{
				FontSize = ToolbarItemTextSize.ToScaledPoint(),
				Text = item.Text,
				TextColor = accentColor,
				HeightSpecification = LayoutParamPolicies.MatchParent,
				WidthSpecification = LayoutParamPolicies.WrapContent,
			};

			button.SizeWidth = (float)button
				.Measure(TDeviceInfo.ScalingFactor * ToolbarItemMaxWidth, double.PositiveInfinity)
				.Width;

			button.UpdateBackgroundColor(TColor.Transparent);
			button.IsEnabled = MenuItemActivation.CanActivate(item);

			if (item.IconImageSource is not null && loadIcon is not null)
			{
				button.Text = string.Empty;
				button.Icon.AdjustViewSize = true;
				button.Icon.HeightSpecification = LayoutParamPolicies.MatchParent;
				button.SizeWidth = 0;
				button.WidthSpecification = LayoutParamPolicies.WrapContent;

				loadIcon(item.IconImageSource, button);
			}

			button.Clicked += (_, _) => MenuItemActivation.Activate(item);

			return button;
		}

	}
}
