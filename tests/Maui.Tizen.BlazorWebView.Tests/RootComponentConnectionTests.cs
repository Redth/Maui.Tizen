using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	public class RootComponentConnectionTests
	{
		[Fact]
		public void ReplacingTheDesiredCollectionUnmountsBeforeAddingTheReplacement()
		{
			var operations = new List<string>();
			var first = Component("#app", typeof(string));
			var second = Component("#app", typeof(int));
			var connection = new RootComponentConnection(
				component =>
				{
					operations.Add("add:" + component.ComponentType!.Name);
					return Task.CompletedTask;
				},
				component =>
				{
					operations.Add("remove:" + component.ComponentType!.Name);
					return Task.CompletedTask;
				},
				work => work());

			connection.UpdateDesired(new[] { first });
			connection.UpdateDesired(new[] { second });

			Assert.Equal(
				new[] { "add:String", "remove:String", "add:Int32" },
				operations);
			Assert.Equal(new[] { second }, connection.Mounted);
		}

		[Fact]
		public async Task RetirementCancelsAnActiveManagerWaitAndRejectsLaterRequests()
		{
			var addStarted = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseAdd = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var addCount = 0;
			var connection = new RootComponentConnection(
				async _ =>
				{
					addCount++;
					addStarted.TrySetResult(null);
					await releaseAdd.Task;
				},
				_ => Task.CompletedTask,
				work => work());

			connection.UpdateDesired(new[] { Component("#first", typeof(string)) });
			await addStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

			var retirement = connection.RetireAsync();
			connection.UpdateDesired(new[] { Component("#late", typeof(int)) });
			await retirement.WaitAsync(TimeSpan.FromSeconds(10));

			releaseAdd.TrySetResult(null);
			await Task.Yield();

			Assert.Equal(1, addCount);
			Assert.Empty(connection.Mounted);
		}

		[Fact]
		public async Task RetiredGenerationCannotMutateTheReplacementGeneration()
		{
			var oldStarted = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var releaseOld = new TaskCompletionSource<object?>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var oldConnection = new RootComponentConnection(
				async _ =>
				{
					oldStarted.TrySetResult(null);
					await releaseOld.Task;
				},
				_ => Task.CompletedTask,
				work => work());
			var replacement = Component("#app", typeof(int));
			var newConnection = new RootComponentConnection(
				_ => Task.CompletedTask,
				_ => Task.CompletedTask,
				work => work());

			oldConnection.UpdateDesired(new[] { Component("#app", typeof(string)) });
			await oldStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
			var retirement = oldConnection.RetireAsync();
			await retirement.WaitAsync(TimeSpan.FromSeconds(10));
			newConnection.UpdateDesired(new[] { replacement });
			releaseOld.TrySetResult(null);
			await Task.Yield();

			Assert.Empty(oldConnection.Mounted);
			Assert.Equal(new[] { replacement }, newConnection.Mounted);
		}

		private static RootComponent Component(string selector, Type type) =>
			new()
			{
				Selector = selector,
				ComponentType = type,
			};
	}
}
