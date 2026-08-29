namespace Maui.Tizen.SourceTests;

/// <summary>
/// Resolves every mapper key a Wave B handler answers to the Tizen method that will actually run,
/// following the inheritance chain.
/// </summary>
/// <remarks>
/// <para>
/// Key-presence parity is not enough on its own. A key can be "present" on a concrete handler purely
/// because it chains a base mapper, and the interesting question is what the chained entry
/// <em>resolves to</em>: a Tizen body, an inert neutral body, or — worst — a mapping that hard-casts
/// to a concrete upstream handler class and throws when handed a Tizen one.
/// </para>
/// <para>
/// The seven concrete shape handlers are the case that motivated this. Six chain
/// <c>TizenShapeViewHandler.Mapper</c> explicitly and <c>TizenBoxViewHandler</c> simply inherits it,
/// so <c>StrokeDashArray</c> is answered by all seven without any of them declaring it. Counting
/// only directly-declared keys makes that look like seven gaps; counting effective keys without
/// checking ownership would hide a neutral body. This resolves both.
/// </para>
/// </remarks>
public class EffectiveMapperTests
{
	/// <summary>A key, and the Tizen handler and method that answer it.</summary>
	public sealed record Resolved(string Key, string DeclaringHandler, string Method, bool IsNoOp);

	static HandlerSource? Find(string typeName) =>
		WaveBSource.Handlers.FirstOrDefault(h => h.TypeName == typeName);

	/// <summary>
	/// Every key <paramref name="handler"/> answers, resolved to its nearest declaration.
	/// </summary>
	/// <remarks>
	/// Nearest wins, matching <c>PropertyMapper</c>: a handler's own entry shadows the one it
	/// chained, so the walk records the first declaration it meets and never overwrites it.
	/// </remarks>
	public static IReadOnlyDictionary<string, Resolved> Resolve(HandlerSource handler)
	{
		var resolved = new Dictionary<string, Resolved>(StringComparer.Ordinal);

		for (HandlerSource? current = handler; current is not null; current = Find(current.BaseType))
		{
			foreach (var entry in current.PropertyMappers.Concat(current.CommandMappers))
			{
				if (!resolved.ContainsKey(entry.Key))
					resolved[entry.Key] = new Resolved(entry.Key, current.TypeName, entry.Method, entry.IsNoOp);
			}

			if (current.BaseType == current.TypeName)
				break;
		}

		return resolved;
	}

	public static readonly string[] ConcreteShapeHandlers =
	{
		"TizenBoxViewHandler",
		"TizenLineHandler",
		"TizenPathHandler",
		"TizenPolygonHandler",
		"TizenPolylineHandler",
		"TizenRectangleHandler",
		"TizenRoundRectangleHandler",
	};

	/// <summary>
	/// All seven concrete shape handlers answer <c>StrokeDashArray</c>, and it resolves to the
	/// Tizen base body rather than to anything upstream.
	/// </summary>
	/// <remarks>
	/// Wave C's regenerated parity reported this key on all seven, which reads as seven gaps if the
	/// report counts only directly-declared keys. None of them declares it; all of them answer it.
	/// </remarks>
	[Theory]
	[MemberData(nameof(ShapeHandlerNames))]
	public void EveryConcreteShapeHandlerResolvesStrokeDashArrayToTheTizenBody(string handlerName)
	{
		var handler = Find(handlerName);
		Assert.NotNull(handler);

		var resolved = Resolve(handler!);

		Assert.True(
			resolved.TryGetValue("StrokeDashArray", out var entry),
			$"{handlerName} answers no StrokeDashArray key. Microsoft.Maui.Controls adds it to the "
			+ "neutral ShapeViewHandler.Mapper, so a Tizen shape that does not answer it silently "
			+ "ignores dashes.");

		Assert.Equal("TizenShapeViewHandler", entry!.DeclaringHandler);
		Assert.Equal("MapStrokeDashArray", entry.Method);
		Assert.False(entry.IsNoOp, "StrokeDashArray must redraw the shape, not be a no-op.");
	}

	public static TheoryData<string> ShapeHandlerNames
	{
		get
		{
			var data = new TheoryData<string>();
			foreach (var name in ConcreteShapeHandlers)
				data.Add(name);
			return data;
		}
	}

	/// <summary>
	/// <c>IconColor</c> is a real mapping, not a no-op.
	/// </summary>
	/// <remarks>
	/// It was recorded as an unsupported no-op on the grounds that the Tizen menu button had no tint
	/// API. That was wrong: <c>Tizen.NUI.Components.Button.Icon</c> is an <c>ImageView</c> and
	/// <c>ImageView.ImageColor</c> multiplies the image by a colour. Upstream's Tizen backend
	/// omitting it was a gap, not a platform limitation.
	/// </remarks>
	[Fact]
	public void IconColorIsARealMapping()
	{
		var handler = Find("TizenSwipeItemMenuItemHandler");
		Assert.NotNull(handler);

		var resolved = Resolve(handler!);

		Assert.True(resolved.TryGetValue("IconColor", out var entry), "IconColor is not mapped.");
		Assert.Equal("MapIconColor", entry!.Method);
		Assert.False(
			entry.IsNoOp,
			"IconColor must tint Button.Icon through ImageView.ImageColor. Tizen supports this, so "
			+ "a no-op here would be an unnecessary gap rather than a platform limitation.");
	}

	/// <summary>
	/// No Wave B mapper may chain a neutral MAUI <em>concrete</em> handler's mapper.
	/// </summary>
	/// <remarks>
	/// This is the crash-safety invariant. <c>PropertyMapper&lt;TVirtualView, TViewHandler&gt;.Add</c>
	/// wraps each mapping in a closure that casts the handler to <c>TViewHandler</c>, guarded only by
	/// the virtual-view type. When <c>TViewHandler</c> is a concrete upstream class such as
	/// <c>LineHandler</c>, dispatching that key onto a Tizen handler throws
	/// <see cref="InvalidCastException"/> — and the key is often reachable only through chaining, so
	/// nothing in the source names it. Chaining only Tizen-owned or interface-typed base mappers is
	/// what keeps that impossible.
	/// </remarks>
	[Fact]
	public void NoWaveBMapperChainsANeutralConcreteHandlerMapper()
	{
		var allowed = new HashSet<string>(StringComparer.Ordinal)
		{
			// Interface-typed neutral base mappers: Action<IViewHandler, IView>, no concrete cast.
			// These become Core's TizenViewMappers equivalents at the Wave A rebase.
			"ViewMapper",
			"ViewCommandMapper",
			"ElementMapper",
			"ElementCommandMapper",
			"ViewHandler.ViewMapper",
			"ViewHandler.ViewCommandMapper",
			"ElementHandler.ElementMapper",
			"ElementHandler.ElementCommandMapper",
		};

		var failures = new List<string>();

		foreach (var handler in WaveBSource.Handlers)
		{
			var source = File.ReadAllText(RepoPaths.Combine(handler.RelativePath.Split('/')));

			foreach (var chained in ChainedMapperNames(source))
			{
				if (allowed.Contains(chained) || chained.StartsWith("Tizen", StringComparison.Ordinal))
					continue;

				failures.Add($"{handler.TypeName} chains '{chained}', which is not a Tizen-owned or interface-typed base mapper ({handler.RelativePath}).");
			}
		}

		Assert.Empty(failures);
	}

	/// <summary>Extracts the argument of every <c>new PropertyMapper&lt;...&gt;(x)</c> / <c>new CommandMapper&lt;...&gt;(x)</c>.</summary>
	static IEnumerable<string> ChainedMapperNames(string source)
	{
		foreach (var marker in new[] { "new PropertyMapper<", "new CommandMapper<", "new(" })
		{
			var index = 0;
			while ((index = source.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
			{
				var open = source.IndexOf('(', index + marker.Length - 1);
				index += marker.Length;

				if (open < 0)
					continue;

				var close = source.IndexOf(')', open);
				if (close < 0)
					continue;

				var argument = source[(open + 1)..close].Trim();

				if (argument.Length > 0 && !argument.Contains(' ', StringComparison.Ordinal))
					yield return argument;
			}
		}
	}
}
