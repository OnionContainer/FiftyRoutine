using System.Windows.Media;

namespace PersonalManagement.Desktop;

/// <summary>进行中的任务执行状态；由主窗口持有，执行窗仅作操作/展示面板。</summary>
public sealed class TaskRunState
{
    private static readonly SolidColorBrush GoldBrush = CreateGold();

    public string TaskId { get; }
    public string SessionId { get; }
    public string TaskTitle { get; }
    public DateTime StartedAt { get; }
    public int RewardMinutes { get; }
    public int RequiredSeconds { get; }
    public List<PauseSpan> Pauses { get; } = [];
    public DateTime? PauseStart { get; private set; }
    public bool NotifiedRequired { get; set; }
    public bool Finished { get; private set; }

    public TaskRunState(string taskId, string sessionId, string title, DateTime startedAt,
        int rewardMinutes, IEnumerable<PauseSpan>? pauses = null)
    {
        TaskId = taskId;
        SessionId = sessionId;
        TaskTitle = title;
        StartedAt = startedAt;
        RewardMinutes = Math.Clamp(rewardMinutes, 1, 1440);
        RequiredSeconds = SessionLogic.RequiredSeconds(RewardMinutes);
        if (pauses is not null) Pauses.AddRange(pauses);
    }

    public bool IsPaused => PauseStart is not null;

    public double ClosedPausedSeconds => Pauses.Sum(p => (p.End - p.Start).TotalSeconds);

    public double LivePausedSeconds =>
        ClosedPausedSeconds + (IsPaused ? (DateTime.Now - PauseStart!.Value).TotalSeconds : 0);

    public double ActiveSeconds =>
        SessionLogic.ActiveSeconds(StartedAt, DateTime.Now, LivePausedSeconds);

    public void TogglePause()
    {
        if (Finished) return;
        if (IsPaused)
        {
            Pauses.Add(new PauseSpan(PauseStart!.Value, DateTime.Now));
            PauseStart = null;
        }
        else
            PauseStart = DateTime.Now;
    }

    public (int Lap, double FracInLap) Progress()
    {
        var active = ActiveSeconds;
        var r = RequiredSeconds;
        var lap = (int)Math.Floor(active / r);
        var frac = active - lap * r;
        return (lap, frac);
    }

    public static Brush BarBrush(int lap)
    {
        if (lap <= 0) return Theme.Brush("AccentBrush");
        if (lap % 2 == 1) return GoldBrush;
        return Theme.Brush("DangerBrush");
    }

    public TaskRunFinish Finish(bool success)
    {
        if (Finished)
            throw new InvalidOperationException("执行已结束");
        Finished = true;
        if (IsPaused)
        {
            Pauses.Add(new PauseSpan(PauseStart!.Value, DateTime.Now));
            PauseStart = null;
        }
        var end = DateTime.Now;
        return new TaskRunFinish
        {
            SessionId = SessionId,
            TaskId = TaskId,
            TaskTitle = TaskTitle,
            Success = success,
            StartedAt = StartedAt,
            EndedAt = end,
            PausedSeconds = ClosedPausedSeconds,
            Pauses = [.. Pauses]
        };
    }

    private static SolidColorBrush CreateGold()
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D4A017")!);
        b.Freeze();
        return b;
    }
}
