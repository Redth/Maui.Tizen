// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Maui.Platforms.Tizen;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class CleanupTests
	{
		[Fact]
		public void EveryCleanupRunsBeforeFailuresAreAggregated()
		{
			var ran = new List<int>();

			var failure = Assert.Throws<AggregateException>(() =>
				TizenCleanup.Run(
					() =>
					{
						ran.Add(1);
						throw new InvalidOperationException("first");
					},
					() => ran.Add(2),
					() =>
					{
						ran.Add(3);
						throw new ArgumentException("third");
					},
					() => ran.Add(4)));

			Assert.Equal([1, 2, 3, 4], ran);
			Assert.Collection(
				failure.InnerExceptions,
				exception => Assert.Equal("first", exception.Message),
				exception => Assert.Equal("third", exception.Message));
		}

		[Fact]
		public void ASingleFailurePreservesItsOriginalType()
		{
			var failure = Assert.Throws<InvalidOperationException>(() =>
				TizenCleanup.Run(
					static () => throw new InvalidOperationException("single"),
					static () => { }));

			Assert.Equal("single", failure.Message);
		}

		[Theory]
		[InlineData("TizenButtonHandler.cs", "_iconLoader.Dispose", "TouchEvent -= OnTouch", "Clicked -= OnClicked")]
		[InlineData("TizenSliderHandler.cs", "_thumbLoader.Dispose", "ValueChanged -= OnControlValueChanged", "SlidingFinished -= OnSlidingFinished")]
		[InlineData("TizenPickerHandler.cs", "_popupLifecycle.CancelOnUiThread", "TouchEvent -= OnTouch", "KeyEvent -= OnKeyEvent")]
		[InlineData("TizenDatePickerHandler.cs", "_popupLifecycle.CancelOnUiThread", "TouchEvent -= OnTouch", "KeyEvent -= OnKeyEvent")]
		[InlineData("TizenTimePickerHandler.cs", "_popupLifecycle.CancelOnUiThread", "TouchEvent -= OnTouch", "KeyEvent -= OnKeyEvent")]
		public void HandlerDisconnectRunsOwnershipEventsAndBaseAsOneCleanup(
			string fileName,
			string ownershipCleanup,
			string firstEventCleanup,
			string lastEventCleanup)
		{
			var source = File.ReadAllText(Path.Combine(
				TestRepositoryPaths.Root,
				"src",
				"Maui.Tizen.Core",
				"Handlers",
				fileName));
			var start = source.IndexOf(
				"protected override void DisconnectHandler",
				StringComparison.Ordinal);
			var end = source.IndexOf(
				"\n\t\tpublic static void ",
				start,
				StringComparison.Ordinal);
			var disconnect = source[start..end];

			Assert.Contains("TizenCleanup.Run(", disconnect, StringComparison.Ordinal);
			Assert.Contains(ownershipCleanup, disconnect, StringComparison.Ordinal);
			Assert.Contains(firstEventCleanup, disconnect, StringComparison.Ordinal);
			Assert.Contains(lastEventCleanup, disconnect, StringComparison.Ordinal);
			Assert.Contains("base.DisconnectHandler(platformView)", disconnect, StringComparison.Ordinal);
		}
	}
}
