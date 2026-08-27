using System;
using System.Linq;
using System.Threading.Tasks;
using global::Tizen.UIExtensions.NUI;
using NView = global::Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Nui
{
	/// <summary>
	/// Adapts <see cref="NavigationStack"/> to the <see cref="ITizenNavigationStack"/> contract.
	/// </summary>
	/// <remarks>
	/// This is the only NUI-aware part of Tizen modal coordination. Everything that decides
	/// <em>when</em> to push or pop - dialog placeholder balance in <see cref="TizenModalHost"/> and
	/// modal page presentation in <see cref="TizenModalNavigationPlatform"/> - works through the
	/// contract and is unit tested on the host.
	/// </remarks>
	public sealed class NuiNavigationStack : ITizenNavigationStack
	{
		readonly NavigationStack _stack;

		/// <summary>
		/// Initializes a new instance of the <see cref="NuiNavigationStack"/> class.
		/// </summary>
		/// <param name="stack">The window's navigation stack.</param>
		public NuiNavigationStack(NavigationStack stack) =>
			_stack = stack ?? throw new ArgumentNullException(nameof(stack));

		/// <inheritdoc/>
		public int Count => _stack.Stack.Count;

		/// <inheritdoc/>
		public object? Top => _stack.Top;

		/// <inheritdoc/>
		public bool Contains(object platformView) =>
			_stack.Stack.Contains(AsView(platformView, nameof(platformView)));

		/// <inheritdoc/>
		public bool ShownBehindPage
		{
			get => _stack.ShownBehindPage;
			set => _stack.ShownBehindPage = value;
		}

		/// <inheritdoc/>
		public object CreatePlaceholder() => new NView();

		/// <inheritdoc/>
		public Task PushAsync(object platformView, bool animated) =>
			_stack.Push(AsView(platformView, nameof(platformView)), animated);

		/// <inheritdoc/>
		public Task PopAsync(bool animated) => _stack.Pop(animated);

		/// <inheritdoc/>
		public bool Remove(object platformView)
		{
			var view = AsView(platformView, nameof(platformView));

			if (ReferenceEquals(_stack.Top, view))
			{
				// Pop(View) does not refresh NavigationStack.Top. Use the normal nonanimated pop
				// for the top entry so the preceding view is shown and focus is restored.
				_stack.Pop(false).GetAwaiter().GetResult();
				return true;
			}

			_stack.Pop(view);
			return false;
		}

		static NView AsView(object platformView, string parameterName)
		{
			ArgumentNullException.ThrowIfNull(platformView, parameterName);

			return platformView as NView
				?? throw new ArgumentException(
					$"The Tizen navigation stack needs a Tizen.NUI.BaseComponents.View but was given a '{platformView.GetType()}'.",
					parameterName);
		}
	}
}
