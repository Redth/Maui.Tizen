// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IProgress"/>.
	/// </summary>
	public class TizenProgressBarHandler : TizenViewHandler<IProgress, TizenProgressBarView>, IProgressBarHandler
	{
		/// <summary>The complete property mapper for <see cref="IProgress"/>.</summary>
		public static readonly IPropertyMapper<IProgress, IProgressBarHandler> Mapper =
			new PropertyMapper<IProgress, IProgressBarHandler>(TizenHandlerMappers.Chain(ProgressBarHandler.Mapper))
			{
				[nameof(IProgress.Progress)] = MapProgress,
				[nameof(IProgress.ProgressColor)] = MapProgressColor,
			};

		/// <summary>The complete command mapper for <see cref="IProgress"/>.</summary>
		public static readonly CommandMapper<IProgress, IProgressBarHandler> CommandMapper =
			new CommandMapper<IProgress, IProgressBarHandler>(TizenHandlerMappers.ChainCommands(ProgressBarHandler.CommandMapper));

		public TizenProgressBarHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenProgressBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IProgress IProgressBarHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="IProgressBarHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object IProgressBarHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="IProgressBarHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenProgressBarView? Platform(IProgressBarHandler handler) => handler.PlatformView as TizenProgressBarView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenProgressBarHandler AsHandler(IProgressBarHandler handler) => (TizenProgressBarHandler)handler;

		protected override TizenProgressBarView CreatePlatformView() => new();

		public static void MapProgress(IProgressBarHandler handler, IProgress progress)
		{
#if TIZEN
			Platform(handler)?.UpdateProgress(progress);
#endif
		}

		public static void MapProgressColor(IProgressBarHandler handler, IProgress progress)
		{
#if TIZEN
			Platform(handler)?.UpdateProgressColor(progress);
#endif
		}
	}
}
