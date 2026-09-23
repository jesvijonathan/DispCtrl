namespace DispCtrl.Control;

/// <summary>A validated operation; discovery and validation happen before execution.</summary>
public sealed record PlannedOperation(int Order, string Name, string? Monitor, Func<bool> Apply);

public sealed record OperationOutcome(int Order, string Name, string? Monitor, string State, string? Error);

/// <summary>Stable phase ordering with failure barriers; hardware changes are not atomic.</summary>
public static class OperationSequence
{
    public static IReadOnlyList<OperationOutcome> Execute(IEnumerable<PlannedOperation> plan, bool dryRun)
    {
        var results = new List<OperationOutcome>();
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool stop = false;
        foreach (var step in plan.OrderBy(s => s.Order))
        {
            string state = "planned";
            string? error = null;
            if (!dryRun)
            {
                if (stop || step.Monitor is not null && blocked.Contains(step.Monitor))
                {
                    state = "skipped";
                    error = "A prerequisite operation failed; retry after checking the current state.";
                }
                else
                {
                    try { state = step.Apply() ? "applied" : "failed"; }
                    catch (Exception ex) { state = "failed"; error = ex.Message; }
                    if (state == "failed")
                    {
                        error ??= "The adapter refused the operation or verification failed.";
                        // Layout and mode failures invalidate the desk's later
                        // coordinate assumptions, even for another monitor.
                        if (step.Monitor is null || step.Order <= 40) stop = true;
                        else blocked.Add(step.Monitor);
                    }
                }
            }
            results.Add(new(step.Order, step.Name, step.Monitor, state, error));
        }
        return results;
    }
}
