using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Navigation;

/// <summary>WinForms navigation kinds, stored in <see cref="NavigationIr.TargetType"/> (a free string in the shared IR).</summary>
public enum WinNavigationKind { Show, ShowDialog, Close, Hide, Exit }

/// <summary>
/// WinForms vocabulary on top of the shared, web-shaped <see cref="NavigationIr"/>, without changing it:
/// <c>SourceControlId</c> = trigger control, <c>TriggerEvent</c> = event (e.g. "Click"),
/// <c>Target</c> = target form (empty for Close/Hide/Exit), <c>TargetType</c> = <see cref="WinNavigationKind"/> name.
/// The handler method is not stored; it is the <see cref="WorkflowIr.HandlerMethod"/> with the same control + event.
/// </summary>
public static class WinNavigation
{
    public static NavigationIr Create(string triggerControlId, string triggerEvent, WinNavigationKind kind, string? targetForm = null, string? semantic = null) =>
        new()
        {
            SourceControlId = triggerControlId,
            TriggerEvent = triggerEvent,
            Target = targetForm ?? string.Empty,
            TargetType = kind.ToString(),
            Semantic = semantic
        };

    /// <summary>True when the entry uses the WinForms vocabulary (as opposed to a web Url/Redirect entry).</summary>
    public static bool TryGetKind(this NavigationIr nav, out WinNavigationKind kind) =>
        Enum.TryParse(nav.TargetType, ignoreCase: false, out kind) && Enum.IsDefined(kind);

    public static string TargetForm(this NavigationIr nav) => nav.Target;

    /// <summary>The workflow handling the same control + event, i.e. the handler that performs this navigation.</summary>
    public static WorkflowIr? FindWorkflow(this NavigationIr nav, PageIr page) =>
        page.Workflows.FirstOrDefault(w => w.TriggerControlId == nav.SourceControlId && w.TriggerEvent == nav.TriggerEvent);
}
