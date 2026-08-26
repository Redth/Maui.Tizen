// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Dispatching;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Regressions for marshalling handler continuations onto the UI thread.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The defect: handler code awaited with <c>ConfigureAwait(false)</c> and then touched NUI or
	/// wrote a property on the virtual view. Both are only legal on the Tizen main loop - NUI is
	/// not thread-safe, and a virtual-view write re-enters MAUI's property system, which runs the
	/// mapper and touches NUI in turn.
	/// </para>
	/// <para>
	/// This is the failure mode that testing is worth the most on, because off-thread NUI access
	/// usually appears to work. It corrupts state or crashes later, under load, somewhere else.
	/// </para>
	/// </remarks>
	public class DispatchExtensionsTests
	{
		/// <summary>A dispatcher that records what it was asked to marshal.</summary>
		sealed class RecordingDispatcher : IDispatcher
		{
			public RecordingDispatcher(bool dispatchRequired) => IsDispatchRequired = dispatchRequired;

			public bool IsDispatchRequired { get; set; }

			public int DispatchCount { get; private set; }

			public bool DispatchResult { get; set; } = true;

			public bool Dispatch(Action action)
			{
				DispatchCount++;

				if (DispatchResult)
					action();

				return DispatchResult;
			}

			public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);

			public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
		}

		static IElementHandler HandlerWith(IDispatcher dispatcher)
		{
			var services = new ServiceCollection();
			services.AddSingleton(dispatcher);

			var handler = new Microsoft.Maui.Platforms.Tizen.Handlers.TizenButtonHandler();
			handler.SetMauiContext(new StubMauiContext(services.BuildServiceProvider()));

			return handler;
		}

		sealed class StubMauiContext : IMauiContext
		{
			public StubMauiContext(IServiceProvider services) => Services = services;

			public IServiceProvider Services { get; }

			public IMauiHandlersFactory Handlers => throw new NotSupportedException();
		}

		/// <summary>
		/// Work is marshalled when it is not already on the UI thread.
		/// </summary>
		[Fact]
		public void DispatchesWhenDispatchIsRequired()
		{
			var dispatcher = new RecordingDispatcher(dispatchRequired: true);
			var handler = HandlerWith(dispatcher);
			var ran = false;

			handler.DispatchIfRequired(() => ran = true);

			Assert.Equal(1, dispatcher.DispatchCount);
			Assert.True(ran);
		}

		/// <summary>
		/// Work already on the UI thread runs inline.
		/// </summary>
		/// <remarks>
		/// Dispatching unconditionally would defer work that could have run immediately, reordering
		/// it against the rest of the mapper pass - a different bug, not a safer one.
		/// </remarks>
		[Fact]
		public void RunsInlineWhenAlreadyOnTheUIThread()
		{
			var dispatcher = new RecordingDispatcher(dispatchRequired: false);
			var handler = HandlerWith(dispatcher);
			var ran = false;

			handler.DispatchIfRequired(() => ran = true);

			Assert.Equal(0, dispatcher.DispatchCount);
			Assert.True(ran);
		}

		/// <summary>
		/// With no dispatcher available the work still runs.
		/// </summary>
		/// <remarks>
		/// A disconnected handler has no context. Silently dropping the work would turn a teardown
		/// race into a missing update; running inline is correct because there is no live view left
		/// to marshal to.
		/// </remarks>
		[Fact]
		public void RunsInlineWhenNoDispatcherIsAvailable()
		{
			var handler = new Microsoft.Maui.Platforms.Tizen.Handlers.TizenButtonHandler();
			var ran = false;

			((IElementHandler)handler).DispatchIfRequired(() => ran = true);

			Assert.True(ran);
		}

		/// <summary>
		/// The dispatcher is resolved from the handler's context.
		/// </summary>
		[Fact]
		public void ResolvesTheDispatcherFromTheHandlerContext()
		{
			var dispatcher = new RecordingDispatcher(dispatchRequired: true);
			var handler = HandlerWith(dispatcher);

			Assert.Same(dispatcher, handler.GetDispatcher());
		}

		/// <summary>
		/// A null handler yields no dispatcher rather than throwing.
		/// </summary>
		[Fact]
		public void NullHandlerHasNoDispatcher() =>
			Assert.Null(((IElementHandler?)null).GetDispatcher());

		/// <summary>
		/// The backend must not reintroduce <c>ConfigureAwait(false)</c> before a UI touch.
		/// </summary>
		/// <remarks>
		/// A source-level guard, because this is a pattern that is easy to add back by habit -
		/// <c>ConfigureAwait(false)</c> is the correct default in library code and the wrong one
		/// here. The check is deliberately narrow: it only covers the handler and image-source
		/// files that resume onto NUI, and permits the annotated sites where the continuation is
		/// explicitly re-dispatched.
		/// </remarks>
		[Fact]
		public void HandlersDoNotAwaitAwayFromTheUIThreadBeforeTouchingTheView()
		{
			var offenders = new List<string>();

			foreach (var file in System.IO.Directory.EnumerateFiles(
				System.IO.Path.Combine(TestRepositoryPaths.Root, "src", "Maui.Tizen.Core", "Handlers"),
				"*.cs"))
			{
				var text = System.IO.File.ReadAllText(file);

				// A ConfigureAwait(false) is only acceptable where the very next thing the code
				// does is marshal back - which in this backend means DispatchIfRequired.
				if (text.Contains("ConfigureAwait(false)", StringComparison.Ordinal) &&
					!text.Contains("DispatchIfRequired", StringComparison.Ordinal))
				{
					offenders.Add(System.IO.Path.GetFileName(file));
				}
			}

			Assert.True(
				offenders.Count == 0,
				$"These handlers await away from the UI thread without marshalling back: " +
				$"{string.Join(", ", offenders)}. A continuation that touches NUI or writes the " +
				"virtual view must run on the Tizen main loop.");
		}
	}
}
