using System.Reflection;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>Identity tokens used to bind native actions to the element captured by DevFlow.</summary>
public static class NativeElementIdentity
{
    static readonly PropertyInfo IdentityTokenProperty =
        typeof(ElementInfo).GetProperty(
            "IdentityToken",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(typeof(ElementInfo).FullName, "IdentityToken");

    public static object Capture(object nativeElement) =>
        nativeElement ?? throw new ArgumentNullException(nameof(nativeElement));

    public static bool Matches(object capturedIdentity, object currentElement) =>
        ReferenceEquals(capturedIdentity, currentElement);

    public static void Stamp(ElementInfo element, object nativeElement)
    {
        ArgumentNullException.ThrowIfNull(element);
        IdentityTokenProperty.SetValue(element, Capture(nativeElement));
    }

    public static object? Read(ElementInfo element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return IdentityTokenProperty.GetValue(element);
    }
}
