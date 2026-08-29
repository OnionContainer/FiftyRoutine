using System.Windows;

namespace PersonalManagement.Desktop;

public partial class TaskRunWindow : Window
{
    private readonly TaskRunState _state;
    private bool _closingForReal;

    public event Func<bool, Task>? FinishedRequested;

    public TaskRunWindow(TaskRunState state)
    {
        InitializeComponent();
        Theme.Tint(this);
        _state = state;
        TitleText.Text = state.TaskTitle;
        HintText.Text = $"满 {state.RewardMinutes} 分钟不会自动停下，只会提醒。暂停的时间不计入执行时长。";
        Bar.Maximum = state.RequiredSeconds;
        Closing += TaskRunWindow_Closing;
        Refresh();
    }

    public void Refresh()
    {
        if (_state.Finished) return;
        var active = _state.ActiveSeconds;
        var r = _state.RequiredSeconds;
        var (lap, frac) = _state.Progress();
        Bar.Value = frac;
        Bar.Foreground = TaskRunState.BarBrush(lap);
        if (_state.IsPaused)
        {
            ClockText.Text = "已暂停 · 有效 " + Format(active);
            PauseButton.Content = "继续";
        }
        else if (active < r)
        {
            ClockText.Text = "剩余 " + Format(r - active);
            PauseButton.Content = "暂停";
        }
        else
        {
            ClockText.Text = $"已满 {_state.RewardMinutes} 分钟 · 有效 " + Format(active);
            PauseButton.Content = "暂停";
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _state.TogglePause();
        Refresh();
        RunStateChanged?.Invoke();
    }

    public event Action? RunStateChanged;

    private void Fail_Click(object sender, RoutedEventArgs e) =>
        _ = FinishedRequested?.Invoke(false);

    private void Success_Click(object sender, RoutedEventArgs e) =>
        _ = FinishedRequested?.Invoke(true);

    private void TaskRunWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closingForReal || _state.Finished) return;
        e.Cancel = true;
        Hide();
    }

    public void CloseForReal()
    {
        _closingForReal = true;
        Closing -= TaskRunWindow_Closing;
        Close();
    }

    private static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }
}

public sealed class TaskRunFinish
{
    public required string SessionId { get; init; }
    public required string TaskId { get; init; }
    public required string TaskTitle { get; init; }
    public required bool Success { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }
    public required double PausedSeconds { get; init; }
    public required List<PauseSpan> Pauses { get; init; }
}
