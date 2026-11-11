using System.Windows.Input;

namespace DeltaTR.App.ViewModels;

public sealed class AsyncCommand(Func<Task> action, Action<Exception> onError, Func<bool>? enabled = null) : ICommand
{
    private bool busy;
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? parameter) => !busy && (enabled?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        busy = true; CommandManager.InvalidateRequerySuggested();
        try { await action(); }
        catch (Exception error) { onError(error); }
        finally { busy = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
