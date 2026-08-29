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
	/// Adapts Shell content items (ShellSection's ShellContents) to the Tizen CollectionView adaptor model.
	/// Used for the top tab bar.
	/// </summary>
	internal class TizenShellContentItemAdaptor : TizenItemTemplateAdaptor
	{
		TizenItemAppearance? _itemAppearance;

		public TizenShellContentItemAdaptor(ShellSection shellSection, IEnumerable items) :
			base(shellSection, items, GetContentItemTemplate())
		{
			ShellSection = shellSection;
		}

		public ShellSection ShellSection { get; }

		public TizenItemAppearance? ItemAppearance
		{
			get => _itemAppearance;
			set => _itemAppearance = value;
		}

		protected override bool IsSelectable => true;

		public override NView CreateNativeView(int index)
		{
			var item = this[index];
			var view = TizenShellContentItemView.GetContentItemView(item!, MauiContext, _itemAppearance);
			view.Parent = ShellSection;
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


		static DataTemplate GetContentItemTemplate()
		{
			return new DataTemplate(() =>
			{
				return new Microsoft.Maui.Controls.StackLayout();
			});
		}
	}
}
