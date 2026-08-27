using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Dispatch tests: these INVOKE mapper keys rather than only checking that keys are present.
/// </summary>
/// <remarks>
/// <para>
/// Key-presence parity cannot catch the failure this file targets. A MAUI mapper field is declared
/// as <c>IPropertyMapper&lt;TVirtualView, TViewHandler&gt;</c>, and
/// <c>PropertyMapper&lt;,&gt;.Add</c> wraps every mapping in a closure that performs
/// <c>(TViewHandler)h</c>. That cast is guarded only by a check on the VIRTUAL VIEW type, never on
/// the handler. So when <c>TViewHandler</c> is a concrete built-in handler, any other handler that
/// reaches the key through chaining throws <see cref="InvalidCastException"/> at runtime — while
/// every key-presence assertion still passes.
/// </para>
/// <para>
/// The stub below stands in for a Tizen handler: it implements <see cref="IViewHandler"/> and
/// derives from no built-in handler, which is exactly the position
/// <c>TizenViewHandler&lt;,&gt;</c> is in.
/// </para>
/// </remarks>
public class MapperDispatchTests
{
	/// <summary>
	/// A handler that implements the interface and nothing else, mirroring the Tizen handlers'
	/// relationship to MAUI's built-in handler classes.
	/// </summary>
	sealed class ForeignViewHandler : IViewHandler
	{
		public bool HasContainer { get; set; }
		public object? ContainerView => null;
		public object? PlatformView => null;
		public IMauiContext? MauiContext => null;
		public IElement? VirtualView { get; private set; }

		IView? IViewHandler.VirtualView => VirtualView as IView;

		public void SetMauiContext(IMauiContext mauiContext) { }
		public void SetVirtualView(IElement view) => VirtualView = view;
		public void UpdateValue(string property) { }
		public void Invoke(string command, object? args) { }
		public void DisconnectHandler() { }
		public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
		public void PlatformArrange(Rect frame) { }
	}

	/// <summary>
	/// Every key reachable through <c>ViewHandler.ViewMapper</c> must dispatch without hard-casting
	/// the handler.
	/// </summary>
	/// <remarks>
	/// Every Wave B view handler chains <c>ViewMapper</c>, so a single concrete-handler mapping
	/// anywhere in it would crash all of them. The mappers are exercised through a real Controls
	/// host, because <c>ConfigureControls</c> contributes keys that are absent otherwise.
	/// <para>
	/// Only <see cref="InvalidCastException"/> fails the test. Other exceptions are expected and
	/// ignored: these mappers run against a stub with no platform view, so <c>NullReferenceException</c>
	/// and friends mean the body was entered — which is the point.
	/// </para>
	/// </remarks>
	[Fact]
	public void EveryViewMapperKeyDispatchesWithoutHardCastingTheHandler()
	{
		ControlsHost.EnsureBuilt();

		var mapper = MapperField("Microsoft.Maui.Handlers.ViewHandler", "ViewMapper");
		var failures = InvokeAll(mapper, new Microsoft.Maui.Controls.Button());

		Assert.Empty(failures);
	}

	/// <summary>
	/// The same guarantee for <c>ElementHandler.ElementMapper</c>, which
	/// <c>TizenSwipeItemMenuItemHandler</c> chains.
	/// </summary>
	[Fact]
	public void EveryElementMapperKeyDispatchesWithoutHardCastingTheHandler()
	{
		ControlsHost.EnsureBuilt();

		var mapper = MapperField("Microsoft.Maui.Handlers.ElementHandler", "ElementMapper");
		var failures = InvokeAll(mapper, new Microsoft.Maui.Controls.Button());

		Assert.Empty(failures);
	}

	/// <summary>
	/// Dispatch every key against the concrete virtual view types Wave B actually handles.
	/// </summary>
	/// <remarks>
	/// The hard cast is only reached when the virtual view matches the mapping's
	/// <c>TVirtualView</c>, so exercising with a single view type could miss a mapping keyed to a
	/// different one. This walks the real Controls types behind the Wave B handlers.
	/// </remarks>
	[Fact]
	public void ViewMapperDispatchesForEveryWaveBVirtualViewType()
	{
		ControlsHost.EnsureBuilt();

		var mapper = MapperField("Microsoft.Maui.Handlers.ViewHandler", "ViewMapper");

		Microsoft.Maui.Controls.Element[] views =
		{
			new Microsoft.Maui.Controls.ScrollView(),
			new Microsoft.Maui.Controls.Border(),
			new Microsoft.Maui.Controls.ContentView(),
			new Microsoft.Maui.Controls.Image(),
			new Microsoft.Maui.Controls.ImageButton(),
			new Microsoft.Maui.Controls.GraphicsView(),
			new Microsoft.Maui.Controls.RefreshView(),
			new Microsoft.Maui.Controls.SwipeView(),
			new Microsoft.Maui.Controls.SwipeItemView(),
			new Microsoft.Maui.Controls.IndicatorView(),
			new Microsoft.Maui.Controls.BoxView(),
			new Microsoft.Maui.Controls.Shapes.Line(),
			new Microsoft.Maui.Controls.Shapes.Path(),
			new Microsoft.Maui.Controls.Shapes.Polygon(),
			new Microsoft.Maui.Controls.Shapes.Polyline(),
			new Microsoft.Maui.Controls.Shapes.Rectangle(),
			new Microsoft.Maui.Controls.Shapes.RoundRectangle(),
		};

		var failures = views.SelectMany(v => InvokeAll(mapper, v)).ToList();

		Assert.Empty(failures);
	}

	/// <summary>
	/// The mappers Wave B chains must contain no concrete-handler mapping.
	/// </summary>
	/// <remarks>
	/// A static counterpart to the dispatch tests above: it fails on a dangerous mapping even if no
	/// virtual view in the suite happens to trigger it. Concrete-handler mappings DO exist in MAUI —
	/// on <c>ApplicationHandler</c>, <c>PickerHandler</c>, <c>ProgressBarHandler</c>,
	/// <c>StepperHandler</c> and <c>CarouselViewHandler</c> — so this is a live hazard, not a
	/// theoretical one. Wave B chains none of them, and this test keeps it that way.
	/// </remarks>
	[Fact]
	public void MappersChainedByWaveBContainNoConcreteHandlerMappings()
	{
		ControlsHost.EnsureBuilt();

		(string Owner, string Field)[] chained =
		{
			("ViewHandler", "ViewMapper"),
			("ViewHandler", "ViewCommandMapper"),
			("ElementHandler", "ElementMapper"),
			("ElementHandler", "ElementCommandMapper"),
		};

		var failures = ControlsHost.AllMappings
			.Where(m => chained.Any(c => c.Owner == m.Owner && c.Field == m.Field))
			.Where(m => m.CastsToConcreteHandler)
			.Select(m => $"{m.Owner}.{m.Field}[{m.Key}] casts to concrete {m.HandlerType!.Name}")
			.ToList();

		Assert.Empty(failures);
	}

	/// <summary>
	/// Documents the concrete-handler mappings that really do exist, so this hazard cannot be
	/// dismissed as hypothetical and so Wave C is warned before it ports CarouselView.
	/// </summary>
	[Fact]
	public void ConcreteHandlerMappingsAreConfinedToHandlersWaveBDoesNotChain()
	{
		ControlsHost.EnsureBuilt();

		var owners = ControlsHost.AllMappings
			.Where(m => m.CastsToConcreteHandler)
			.Select(m => m.Owner)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(o => o, StringComparer.Ordinal)
			.ToList();

		// If this ever shrinks to nothing the hazard is gone and these tests can be revisited.
		Assert.NotEmpty(owners);

		string[] waveBChains = { "ViewHandler", "ElementHandler" };
		foreach (var chained in waveBChains)
		{
			Assert.DoesNotContain(chained, owners);
		}
	}

	static object MapperField(string typeName, string fieldName)
	{
		var type = typeof(Microsoft.Maui.IView).Assembly.GetType(typeName);
		Assert.NotNull(type);

		var field = type!.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(field);

		var value = field!.GetValue(null);
		Assert.NotNull(value);

		return value!;
	}

	/// <summary>
	/// Invokes every key on <paramref name="mapper"/> and reports only hard-cast failures.
	/// </summary>
	static List<string> InvokeAll(object mapper, IElement virtualView)
	{
		var handler = new ForeignViewHandler();
		handler.SetVirtualView(virtualView);

		var typed = (IPropertyMapper)mapper;
		var failures = new List<string>();

		foreach (var key in typed.GetKeys().Distinct(StringComparer.Ordinal))
		{
			try
			{
				typed.UpdateProperty(handler, virtualView, key);
			}
			catch (InvalidCastException ex)
			{
				failures.Add($"{virtualView.GetType().Name}[{key}]: {ex.Message}");
			}
			catch (Exception)
			{
				// Expected: the stub has no platform view, so mapper bodies fault once entered.
				// Only a hard cast on the HANDLER is a Wave B defect.
			}
		}

		return failures;
	}

	/// <summary>
	/// Keys that only exist once Controls has remapped must be re-declared by Wave B.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>StrokeDashArray</c> is added by <c>Shape.RemapForControls()</c> to the NEUTRAL
	/// <c>ShapeViewHandler.Mapper</c>. Wave B mirrors that handler rather than chaining it, so the
	/// key is invisible until re-declared — before which setting the property did nothing at all.
	/// </para>
	/// <para>
	/// Deliberately asserts on the Tizen mapper, not by dispatching the neutral one. The neutral
	/// shape mapper casts the handler to <c>IShapeViewHandler</c>, so dispatching it with any
	/// handler that does not implement that interface throws — which is a neat demonstration of why
	/// mirroring rather than chaining is what keeps Wave B safe here, and is covered by
	/// <see cref="MappersChainedByWaveBContainNoConcreteHandlerMappings"/>.
	/// </para>
	/// </remarks>
	[Fact]
	public void ControlsContributedShapeKeysAreMappedByWaveB()
	{
		ControlsHost.EnsureBuilt();

		var shapeHandler = WaveBSource.Handlers.Single(h => h.TypeName == "TizenShapeViewHandler");

		Assert.Contains("StrokeDashArray", shapeHandler.PropertyMappers.Select(m => m.Key));

		// Inherited by every shape handler through TizenShapeViewHandler.Mapper.
		var shapeHandlers = WaveBSource.Handlers
			.Where(h => h.BaseType == "TizenShapeViewHandler")
			.ToList();

		Assert.NotEmpty(shapeHandlers);
	}

	/// <summary>
	/// Every Controls-contributed key on a mapper Wave B mirrors must be re-declared by Wave B.
	/// </summary>
	/// <remarks>
	/// Generalises the check above. Wave B mirrors neutral handlers rather than chaining them, so
	/// any key Controls adds to a neutral mapper is invisible to Wave B until it is re-declared.
	/// This is how <c>StrokeDashArray</c> and <c>IconColor</c> were silently unmapped.
	/// </remarks>
	[Fact]
	public void ControlsContributedKeysAreMirroredByWaveB()
	{
		ControlsHost.EnsureBuilt();

		(string Neutral, string Tizen)[] mirrored =
		{
			("ShapeViewHandler", "TizenShapeViewHandler"),
			("SwipeItemMenuItemHandler", "TizenSwipeItemMenuItemHandler"),
		};

		var failures = new List<string>();

		foreach (var (neutralName, tizenName) in mirrored)
		{
			var neutral = NeutralMaui.FindHandler(neutralName);
			Assert.NotNull(neutral);

			var tizen = WaveBSource.Handlers.Single(h => h.TypeName == tizenName);
			var declared = tizen.PropertyMappers.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);

			foreach (var key in NeutralMaui.MapperKeys(neutral!, "Mapper"))
			{
				if (!declared.Contains(key) && !NeutralMaui.ViewMapperKeys.Contains(key))
					failures.Add($"{tizenName} does not mirror '{key}' from {neutralName}.");
			}
		}

		Assert.Empty(failures);
	}
}
