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
	public class TizenProgressBarHandler : TizenViewHandler<IProgress, TizenProgressBarView>
	{
		/// <summary>The complete property mapper for <see cref="IProgress"/>.</summary>
		public static readonly IPropertyMapper<IProgress, TizenProgressBarHandler> Mapper =
			new PropertyMapper<IProgress, TizenProgressBarHandler>(ViewHandler.ViewMapper)
			{
				[nameof(IProgress.Progress)] = MapProgress,
				[nameof(IProgress.ProgressColor)] = MapProgressColor,
			};

		/// <summary>The complete command mapper for <see cref="IProgress"/>.</summary>
		public static readonly CommandMapper<IProgress, TizenProgressBarHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		public TizenProgressBarHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenProgressBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenProgressBarView CreatePlatformView() => new();

		public static void MapProgress(TizenProgressBarHandler handler, IProgress progress)
		{
#if TIZEN
			handler.PlatformView?.UpdateProgress(progress);
#endif
		}

		public static void MapProgressColor(TizenProgressBarHandler handler, IProgress progress)
		{
#if TIZEN
			handler.PlatformView?.UpdateProgressColor(progress);
#endif
		}
	}
}
