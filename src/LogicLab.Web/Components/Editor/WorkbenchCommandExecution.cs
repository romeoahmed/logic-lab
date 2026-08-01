namespace LogicLab.Web.Components.Editor;

public enum WorkbenchCommandKind
{
    Create,
    Author,
    Compile,
    CreateSession,
    ScheduleStimulus,
    Step,
}

internal sealed class WorkbenchCommandExecution
{
    public WorkbenchCommandKind? ActiveCommand { get; private set; }

    public async Task RunAsync(WorkbenchCommandKind command, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (ActiveCommand is not null)
        {
            return;
        }

        ActiveCommand = command;
        try
        {
            await operation();
        }
        finally
        {
            ActiveCommand = null;
        }
    }
}
