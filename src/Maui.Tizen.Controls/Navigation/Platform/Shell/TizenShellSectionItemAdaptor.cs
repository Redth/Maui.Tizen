using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Shell section items (ShellItem's ShellSections) to the Tizen CollectionView adaptor model.
	/// Used for the bottom tab bar.
	/// </summary>
	internal class TizenShellSectionItemAdaptor : TizenItemTemplateAdaptor
	{
		TizenItemAppearance? _itemAppearance;

		public TizenShellSectionItemAdaptor(ShellItem shellItem, IEnumerable items) :
			base(shellItem, items, GetSectionItemTemplate())
		{
			ShellItem = shellItem;
		}

		public ShellItem ShellItem { get; }

		public TizenItemAppearance? ItemAppearance
		{
			get => _itemAppearance;
			set => _itemAppearance = value;
		}

		protected override bool IsSelectable => true;

		public override NView CreateNativeView(int index)
		{
			var item = this[index];
			var view = TizenShellSectionItemView.GetSectionItemView(item!, MauiContext, _itemAppearance);
			view.Parent = ShellItem;
			view.BindingContext = item;
			var native = view.ToPlatformView(MauiContext);

			// Register native-to-MAUI mapping for selection state tracking
			RegisterNativeView(native, view);

			return native;
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
			UnBinding(native);
			// Unregister rather than just look up: leaving the entry behind keeps the view alive and
			// lets a recycled native view resolve to a MAUI view whose handler is already disposed.
			if (UnregisterNativeView(native) is { } view)
			{
				(view.Handler as IDisposable)?.Dispose();
				view.Handler = null;
			}
		}


		static DataTemplate GetSectionItemTemplate()
		{
			return new DataTemplate(() =>
			{
				return new Microsoft.Maui.Controls.StackLayout();
			});
		}
	}
}
