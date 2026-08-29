// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Regressions for text cursor and selection proxying, and for composite-control focus.
	/// </summary>
	public class TextAndFocusProxyTests
	{
		// ------------------------------------------------------------------------------
		// Cursor and selection translation (review item 5)
		// ------------------------------------------------------------------------------

		/// <summary>
		/// A forward drag maps straight through.
		/// </summary>
		[Fact]
		public void ForwardSelectionMapsToStartAndLength()
		{
			var (cursor, length) = TizenTextSelection.Normalize(start: 2, end: 7);

			Assert.Equal(2, cursor);
			Assert.Equal(5, length);
		}

		/// <summary>
		/// A right-to-left drag must not produce a negative length.
		/// </summary>
		/// <remarks>
		/// NUI reports the offsets in drag order, so a backwards drag gives end &lt; start. MAUI
		/// models a selection as a start plus a non-negative length, so passing the raw pair
		/// through would hand MAUI a negative length for an ordinary gesture.
		/// </remarks>
		[Fact]
		public void BackwardSelectionIsNormalised()
		{
			var (cursor, length) = TizenTextSelection.Normalize(start: 7, end: 2);

			Assert.Equal(2, cursor);
			Assert.Equal(5, length);
		}

		/// <summary>
		/// A caret is a zero-length selection, not a negative one.
		/// </summary>
		[Fact]
		public void EmptySelectionIsACaret()
		{
			var (cursor, length) = TizenTextSelection.Normalize(start: 4, end: 4);

			Assert.Equal(4, cursor);
			Assert.Equal(0, length);
		}

		[Fact]
		public void ApplySelectionWritesBothPropertiesToTheTextInput()
		{
			var entry = new StubEntry { Text = "hello world" };

			((ITextInput)entry).ApplySelection(start: 9, end: 3);

			Assert.Equal(3, entry.CursorPosition);
			Assert.Equal(6, entry.SelectionLength);
		}

		[Fact]
		public void ApplyCaretCollapsesTheSelection()
		{
			var entry = new StubEntry { Text = "hello", CursorPosition = 1, SelectionLength = 3 };

			((ITextInput)entry).ApplyCaret(cursorPosition: 4);

			Assert.Equal(4, entry.CursorPosition);
			Assert.Equal(0, entry.SelectionLength);
		}

		/// <summary>
		/// Editor and search bar must subscribe to the caret and selection events.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Only <c>Entry</c> proxied these. Without them the virtual view's
		/// <see cref="ITextInput.CursorPosition"/> and <see cref="ITextInput.SelectionLength"/>
		/// only ever move when the application sets them - so moving the caret in the control
		/// leaves MAUI believing it is still where it was last told, and the next programmatic
		/// edit lands at the wrong offset.
		/// </para>
		/// <para>
		/// Asserted at the source level because the events are raised by NUI, which cannot run in
		/// this lane. The check is that the wiring exists and is symmetric - subscribing without
		/// unsubscribing would leak the handler through the platform view.
		/// </para>
		/// </remarks>
		[Theory]
		[InlineData("TizenEditorHandler.cs")]
		[InlineData("TizenSearchBarHandler.cs")]
		[InlineData("TizenEntryHandler.cs")]
		public void TextHandlersProxyCursorAndSelectionEvents(string fileName)
		{
			var source = System.IO.File.ReadAllText(
				System.IO.Path.Combine(TestRepositoryPaths.Root, "src", "Maui.Tizen.Core", "Handlers", fileName));

			foreach (var evt in new[] { "CursorPositionChanged", "SelectionChanged", "SelectionCleared" })
			{
				Assert.True(
					source.Contains($"{evt} += ", StringComparison.Ordinal),
					$"{fileName} does not subscribe to {evt}, so the virtual view's cursor and " +
					"selection will never reflect what the user did in the control.");

				Assert.True(
					source.Contains($"{evt} -= ", StringComparison.Ordinal),
					$"{fileName} subscribes to {evt} but never unsubscribes, which leaks the " +
					"handler through the platform view.");
			}
		}

		// ------------------------------------------------------------------------------
		// Composite focus forwarding (review item 6)
		// ------------------------------------------------------------------------------

		/// <summary>
		/// The composite controls override focus rather than inheriting the base mapping.
		/// </summary>
		/// <remarks>
		/// A search bar and a stepper are groups: the group itself draws no caret and accepts no
		/// input, so the inherited mapping would focus something that cannot show focus and the
		/// request would appear to succeed while doing nothing.
		/// </remarks>
		[Theory]
		[InlineData(typeof(TizenSearchBarHandler))]
		[InlineData(typeof(TizenStepperHandler))]
		public void CompositeHandlersOverrideTheFocusCommands(Type handlerType)
		{
			var field = handlerType.GetField("CommandMapper")
				?? throw new InvalidOperationException($"{handlerType.Name} has no CommandMapper.");

			var mapper = field.GetValue(null)
				?? throw new InvalidOperationException($"{handlerType.Name}.CommandMapper is null.");

			// The override is declared on the handler itself, not inherited from the base mapper.
			foreach (var command in new[] { nameof(IView.Focus), nameof(IView.Unfocus) })
			{
				var method = handlerType.GetMethod($"Map{command}");

				Assert.True(
					method is not null,
					$"{handlerType.Name} does not declare Map{command}. A composite control must " +
					"forward focus to the child that can actually accept it.");
			}

			Assert.NotNull(mapper);
		}

		/// <summary>
		/// Focusing a composite must not throw and must resolve the request.
		/// </summary>
		/// <remarks>
		/// A <see cref="FocusRequest"/> that is never completed leaves an awaiting caller hanging
		/// forever, so the mapping has to resolve it even when focus cannot be taken.
		/// </remarks>
		[Theory]
		[InlineData(typeof(TizenSearchBarHandler))]
		[InlineData(typeof(TizenStepperHandler))]
		public void FocusingACompositeAlwaysCompletesTheRequest(Type handlerType)
		{
			var handlerCase = TizenControlHandlers.All.Single(h => h.HandlerType == handlerType);
			var handler = (IElementHandler)Activator.CreateInstance(handlerType)!;
			handler.SetVirtualView(StubViews.For(handlerCase.VirtualViewType));

			var request = new FocusRequest();

			var exception = Record.Exception(() => handler.Invoke(nameof(IView.Focus), request));

			Assert.Null(exception);

			// A FocusRequest that is never resolved leaves an awaiting caller hanging forever, so
			// the mapping must complete it even when focus cannot be taken. Reading Result throws
			// when nothing was set, which is precisely the condition under test.
			var unresolved = Record.Exception(() => _ = request.Result);

			Assert.True(
				unresolved is null,
				"The focus request was never resolved. Focus cannot be taken off-device, but the " +
				"mapping must still complete the request rather than leave a caller awaiting it.");
		}

		/// <summary>
		/// The composite platform views expose focus forwarding and child-focus reporting.
		/// </summary>
		/// <remarks>
		/// Asserted at the source level: the members are NUI-typed, so they only exist in the
		/// Tizen compilation, but their absence is exactly the defect under review.
		/// </remarks>
		[Theory]
		[InlineData("TizenSearchBarView.cs", "FocusEntry", "UnfocusEntry", "EntryFocused", "EntryUnfocused")]
		[InlineData("TizenStepperView.cs", "FocusButton", "UnfocusButton", "ButtonFocused", "ButtonUnfocused")]
		public void CompositeViewsForwardFocusToTheirChildren(string fileName, params string[] members)
		{
			var source = System.IO.File.ReadAllText(
				System.IO.Path.Combine(TestRepositoryPaths.Root, "src", "Maui.Tizen.Core", "Platform", "Tizen", fileName));

			foreach (var member in members)
			{
				Assert.True(
					source.Contains(member, StringComparison.Ordinal),
					$"{fileName} does not define {member}. Focus lands on the interactive child of " +
					"a composite, so it has to be forwarded there and reported back.");
			}
		}
	}
}
