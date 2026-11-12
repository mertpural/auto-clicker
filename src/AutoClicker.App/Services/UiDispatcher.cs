using Avalonia.Threading;

namespace AutoClicker.App.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class UiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
