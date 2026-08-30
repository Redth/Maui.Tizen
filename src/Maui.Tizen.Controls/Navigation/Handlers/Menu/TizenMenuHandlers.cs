using System;
using Microsoft.Maui.Handlers;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Describes how much of the menu surface Tizen actually implements.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tizen's NUI shell has no menu bar and no context menu primitive. The in-tree backend
	/// acknowledged this by throwing <see cref="NotImplementedException"/> from
	/// <c>CreatePlatformElement</c> on every menu handler, which meant that simply placing a
	/// <c>MenuBar</c> in a window crashed the app at handler-creation time.
	/// </para>
	/// <para>
	/// This migration keeps the capability gap but changes the failure mode: the handlers create an
	/// inert, zero-sized platform view so that an app authored cross-platform renders without its
	/// menus rather than terminating. The gap stays discoverable through
	/// <see cref="IsMenuBarSupported"/> / <see cref="IsMenuFlyoutSupported"/> and through the
	/// <c>Unsupported</c> classifications in <c>Parity/MapperParity.json</c>.
	/// </para>
	/// <para>
	/// This is a deliberate behavioural divergence from the in-tree backend and is called out as
	/// such in the migration status report.
	/// </para>
	/// </remarks>
	public static class TizenMenuSupport
	{
		/// <summary>
		/// Always <see langword="false"/>: Tizen NUI has no menu bar.
		/// </summary>
		public static bool IsMenuBarSupported => false;

		/// <summary>
		/// Always <see langword="false"/>: Tizen NUI has no context menu primitive.
		/// </summary>
		public static bool IsMenuFlyoutSupported => false;

		/// <summary>
		/// Creates the inert placeholder used by every unsupported menu handler.
		/// </summary>
		internal static NView CreateInertPlatformView() => new()
		{
			SizeWidth = 0,
			SizeHeight = 0,
		};
	}

	/// <summary>
	/// Tizen handler for <see cref="IMenuBar"/>. Renders nothing; see <see cref="TizenMenuSupport"/>.
	/// </summary>
	public partial class TizenMenuBarHandler : ElementHandler<IMenuBar, NView>, IMenuBarHandler
	{
		public static IPropertyMapper<IMenuBar, TizenMenuBarHandler> Mapper =
			new PropertyMapper<IMenuBar, TizenMenuBarHandler>(ElementMapper)
			{
				[nameof(IMenuBar.IsEnabled)] = MapIsEnabled,
			};

		public static CommandMapper<IMenuBar, TizenMenuBarHandler> CommandMapper =
			new(ElementCommandMapper)
			{
				[nameof(Add)] = MapCollectionChange,
				[nameof(Remove)] = MapCollectionChange,
				[nameof(Clear)] = MapCollectionChange,
				[nameof(Insert)] = MapCollectionChange,
			};

		public TizenMenuBarHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenMenuBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IMenuBar IMenuBarHandler.VirtualView => VirtualView;

		object IMenuBarHandler.PlatformView => PlatformView;

		protected override NView CreatePlatformElement() => TizenMenuSupport.CreateInertPlatformView();

		/// <summary>
		/// Unsupported: IsEnabled has no effect because Tizen NUI ships no menu bar widget, so there
		/// is no menu bar surface whose interactivity could be toggled.
		/// </summary>
		public static void MapIsEnabled(TizenMenuBarHandler handler, IMenuBar view)
		{
		}

		public void Add(IMenuBarItem view)
		{
		}

		public void Remove(IMenuBarItem view)
		{
		}

		public void Clear()
		{
		}

		public void Insert(int index, IMenuBarItem view)
		{
		}

		/// <summary>Unsupported: Tizen NUI has no menu bar collection to mutate.</summary>
		/// <remarks>
		/// Add, Remove, Clear, and Insert cannot change a native menu bar because Tizen NUI exposes
		/// no menu bar surface.
		/// </remarks>
		public static void MapCollectionChange(TizenMenuBarHandler handler, IMenuBar view, object? args)
		{
		}
	}

	/// <summary>
	/// Tizen handler for <see cref="IMenuBarItem"/>. Renders nothing; see <see cref="TizenMenuSupport"/>.
	/// </summary>
	public partial class TizenMenuBarItemHandler : ElementHandler<IMenuBarItem, NView>, IMenuBarItemHandler
	{
		public static IPropertyMapper<IMenuBarItem, TizenMenuBarItemHandler> Mapper =
			new PropertyMapper<IMenuBarItem, TizenMenuBarItemHandler>(ElementMapper)
			{
				[nameof(IMenuBarItem.Text)] = MapText,
				[nameof(IMenuBarItem.IsEnabled)] = MapIsEnabled,
			};

		public static CommandMapper<IMenuBarItem, TizenMenuBarItemHandler> CommandMapper =
			new(ElementCommandMapper)
			{
				[nameof(Add)] = MapCollectionChange,
				[nameof(Remove)] = MapCollectionChange,
				[nameof(Clear)] = MapCollectionChange,
				[nameof(Insert)] = MapCollectionChange,
			};

		public TizenMenuBarItemHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenMenuBarItemHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IMenuBarItem IMenuBarItemHandler.VirtualView => VirtualView;

		object IMenuBarItemHandler.PlatformView => PlatformView;

		protected override NView CreatePlatformElement() => TizenMenuSupport.CreateInertPlatformView();

		/// <summary>
		/// Unsupported: Text has no effect because Tizen NUI ships no menu bar, so there is no menu
		/// bar item label to render the string into.
		/// </summary>
		public static void MapText(TizenMenuBarItemHandler handler, IMenuBarItem view)
		{
		}

		/// <summary>
		/// Unsupported: IsEnabled has no effect because Tizen NUI ships no menu bar, so there is no
		/// menu bar item whose interactivity could be toggled.
		/// </summary>
		public static void MapIsEnabled(TizenMenuBarItemHandler handler, IMenuBarItem view)
		{
		}

		public void Add(IMenuElement view)
		{
		}

		public void Remove(IMenuElement view)
		{
		}

		public void Clear()
		{
		}

		public void Insert(int index, IMenuElement view)
		{
		}

		/// <summary>Unsupported: Tizen NUI has no menu bar item collection to mutate.</summary>
		/// <remarks>
		/// Add, Remove, Clear, and Insert cannot change native menu-bar children because Tizen NUI
		/// exposes no menu bar surface.
		/// </remarks>
		public static void MapCollectionChange(TizenMenuBarItemHandler handler, IMenuBarItem view, object? args)
		{
		}
	}

	/// <summary>
	/// Tizen handler for <see cref="IMenuFlyout"/>. Renders nothing; see <see cref="TizenMenuSupport"/>.
	/// </summary>
	public partial class TizenMenuFlyoutHandler : ElementHandler<IMenuFlyout, NView>, IMenuFlyoutHandler
	{
		public static IPropertyMapper<IMenuFlyout, TizenMenuFlyoutHandler> Mapper =
			new PropertyMapper<IMenuFlyout, TizenMenuFlyoutHandler>(ElementMapper);

		public static CommandMapper<IMenuFlyout, TizenMenuFlyoutHandler> CommandMapper =
			new(ElementCommandMapper)
			{
				[nameof(Add)] = MapCollectionChange,
				[nameof(Remove)] = MapCollectionChange,
				[nameof(Clear)] = MapCollectionChange,
				[nameof(Insert)] = MapCollectionChange,
			};

		public TizenMenuFlyoutHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenMenuFlyoutHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IMenuFlyout IMenuFlyoutHandler.VirtualView => VirtualView;

		object IMenuFlyoutHandler.PlatformView => PlatformView;

		protected override NView CreatePlatformElement() => TizenMenuSupport.CreateInertPlatformView();

		public void Add(IMenuElement view)
		{
		}

		public void Remove(IMenuElement view)
		{
		}

		public void Clear()
		{
		}

		public void Insert(int index, IMenuElement view)
		{
		}

		/// <summary>Unsupported: Tizen NUI has no menu flyout collection to mutate.</summary>
		/// <remarks>
		/// Add, Remove, Clear, and Insert cannot change native flyout children because Tizen NUI
		/// exposes no context-menu surface.
		/// </remarks>
		public static void MapCollectionChange(TizenMenuFlyoutHandler handler, IMenuFlyout view, object? args)
		{
		}
	}

	/// <summary>
	/// Tizen handler for <see cref="IMenuFlyoutItem"/>. Renders nothing; see <see cref="TizenMenuSupport"/>.
	/// </summary>
	public partial class TizenMenuFlyoutItemHandler : ElementHandler<IMenuFlyoutItem, NView>, IMenuFlyoutItemHandler
	{
		public static IPropertyMapper<IMenuFlyoutItem, TizenMenuFlyoutItemHandler> Mapper =
			new PropertyMapper<IMenuFlyoutItem, TizenMenuFlyoutItemHandler>(ElementMapper)
			{
				[nameof(IMenuFlyoutItem.Text)] = MapText,
				[nameof(IMenuFlyoutItem.IsEnabled)] = MapIsEnabled,
			};

		public static CommandMapper<IMenuFlyoutItem, TizenMenuFlyoutItemHandler> CommandMapper = new(ElementCommandMapper);

		public TizenMenuFlyoutItemHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenMenuFlyoutItemHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IMenuFlyoutItem IMenuFlyoutItemHandler.VirtualView => VirtualView;

		object IMenuFlyoutItemHandler.PlatformView => PlatformView;

		protected override NView CreatePlatformElement() => TizenMenuSupport.CreateInertPlatformView();

		/// <summary>
		/// Unsupported: Text has no effect because Tizen NUI ships no context-menu primitive, so
		/// there is no menu flyout item label to render the string into.
		/// </summary>
		public static void MapText(TizenMenuFlyoutItemHandler handler, IMenuFlyoutItem view)
		{
		}

		/// <summary>
		/// Unsupported: IsEnabled has no effect because Tizen NUI ships no context-menu primitive, so
		/// there is no menu flyout item whose interactivity could be toggled.
		/// </summary>
		public static void MapIsEnabled(TizenMenuFlyoutItemHandler handler, IMenuFlyoutItem view)
		{
		}
	}

	/// <summary>
	/// Tizen handler for <see cref="IMenuFlyoutSubItem"/>. Renders nothing; see <see cref="TizenMenuSupport"/>.
	/// </summary>
	public partial class TizenMenuFlyoutSubItemHandler : ElementHandler<IMenuFlyoutSubItem, NView>, IMenuFlyoutSubItemHandler
	{
		public static IPropertyMapper<IMenuFlyoutSubItem, TizenMenuFlyoutSubItemHandler> Mapper =
			new PropertyMapper<IMenuFlyoutSubItem, TizenMenuFlyoutSubItemHandler>(ElementMapper)
			{
				[nameof(IMenuFlyoutSubItem.Text)] = MapText,
			};

		public static CommandMapper<IMenuFlyoutSubItem, TizenMenuFlyoutSubItemHandler> CommandMapper =
			new(ElementCommandMapper)
			{
				[nameof(Add)] = MapCollectionChange,
				[nameof(Remove)] = MapCollectionChange,
				[nameof(Clear)] = MapCollectionChange,
				[nameof(Insert)] = MapCollectionChange,
			};

		public TizenMenuFlyoutSubItemHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenMenuFlyoutSubItemHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IMenuFlyoutSubItem IMenuFlyoutSubItemHandler.VirtualView => VirtualView;

		object IMenuFlyoutSubItemHandler.PlatformView => PlatformView;

		protected override NView CreatePlatformElement() => TizenMenuSupport.CreateInertPlatformView();

		/// <summary>
		/// Unsupported: Text has no effect because Tizen NUI ships no context-menu primitive, so there
		/// is no submenu header to render the string into.
		/// </summary>
		public static void MapText(TizenMenuFlyoutSubItemHandler handler, IMenuFlyoutSubItem view)
		{
		}

		public void Add(IMenuElement view)
		{
		}

		public void Remove(IMenuElement view)
		{
		}

		public void Clear()
		{
		}

		public void Insert(int index, IMenuElement view)
		{
		}

		/// <summary>Unsupported: Tizen NUI has no menu flyout sub-item collection to mutate.</summary>
		/// <remarks>
		/// Add, Remove, Clear, and Insert cannot change native submenu children because Tizen NUI
		/// exposes no context-menu surface.
		/// </remarks>
		public static void MapCollectionChange(TizenMenuFlyoutSubItemHandler handler, IMenuFlyoutSubItem view, object? args)
		{
		}
	}

	/// <summary>
	/// Tizen handler for <see cref="IMenuFlyoutSeparator"/>. Renders nothing; see <see cref="TizenMenuSupport"/>.
	/// </summary>
	public partial class TizenMenuFlyoutSeparatorHandler : ElementHandler<IMenuFlyoutSeparator, NView>, IMenuFlyoutSeparatorHandler
	{
		public static IPropertyMapper<IMenuFlyoutSeparator, TizenMenuFlyoutSeparatorHandler> Mapper =
			new PropertyMapper<IMenuFlyoutSeparator, TizenMenuFlyoutSeparatorHandler>(ElementMapper);

		public static CommandMapper<IMenuFlyoutSeparator, TizenMenuFlyoutSeparatorHandler> CommandMapper = new(ElementCommandMapper);

		public TizenMenuFlyoutSeparatorHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenMenuFlyoutSeparatorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IMenuFlyoutSeparator IMenuFlyoutSeparatorHandler.VirtualView => VirtualView;

		object IMenuFlyoutSeparatorHandler.PlatformView => PlatformView;

		protected override NView CreatePlatformElement() => TizenMenuSupport.CreateInertPlatformView();
	}
}
