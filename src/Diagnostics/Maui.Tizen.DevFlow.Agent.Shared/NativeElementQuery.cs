using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Css;

namespace Maui.Tizen.DevFlow.Agent;

/// <summary>Applies DevFlow CSS and legacy filters to a projected native element tree.</summary>
public static class NativeElementQuery
{
    const string QueryRootId = "__tizen_native_query_root__";

    public static List<ElementInfo> Apply(
        List<ElementInfo> tree,
        string? type,
        string? automationId,
        string? text,
        string? selector)
    {
        ArgumentNullException.ThrowIfNull(tree);

        if (!string.IsNullOrWhiteSpace(selector))
        {
            var queryRoot = new ElementInfo
            {
                Id = QueryRootId,
                Type = "NativeRoot",
                FullType = "Maui.Tizen.DevFlow.Agent.NativeRoot",
                IsVisible = true,
                IsEnabled = true,
                Children = tree,
            };

            return
            [
                .. CssSelectorEngine.Query([queryRoot], selector)
                    .Where(info => info.Id != QueryRootId)
                    .Where(info => Matches(info, type, automationId, text))
            ];
        }

        return
        [
            .. Flatten(tree)
                .Where(info => Matches(info, type, automationId, text))
                .OrderByDescending(info => info.AutomationId is not null)
                .ThenByDescending(info => info.Traits?.Contains("actionable") ?? false)
                .ThenBy(info => info.Id, StringComparer.Ordinal)
        ];
    }

    static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> elements)
    {
        foreach (var element in elements)
        {
            yield return element;

            if (element.Children is not null)
            {
                foreach (var child in Flatten(element.Children))
                    yield return child;
            }
        }
    }

    static bool Matches(ElementInfo info, string? type, string? automationId, string? text)
    {
        if (!string.IsNullOrWhiteSpace(type) &&
            !info.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
            !(info.FullType?.EndsWith(type, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(automationId) &&
            !string.Equals(info.AutomationId, automationId, StringComparison.OrdinalIgnoreCase) &&
            !info.Id.Equals(automationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(text) ||
               (info.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (info.Value?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
