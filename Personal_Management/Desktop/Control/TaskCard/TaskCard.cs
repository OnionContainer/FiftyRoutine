using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PersonalManagement.Desktop;

/// <summary>日程任务卡：缩略图/纹样、溢出条、记录/开始按钮。临时 UI，不写盘。</summary>
internal static class TaskCard
{
    public sealed class Handlers
    {
        public required bool Selected { get; init; }
        public required Action OnSelect { get; init; }
        public required Func<Task> OnEdit { get; init; }
        public required Func<Task> OnStart { get; init; }
        public required Func<Task> OnRecord { get; init; }
    }

    public static UIElement Build(TaskRow task, Handlers handlers)
    {
        var t = Theme.Current;
        var w = t.ScheduleCardWidth;
        var thumbH = t.ScheduleCardThumbHeight;
        var btn = t.StartTaskButtonSize;

        var playImg = new Image
        {
            Source = Theme.LoadStartIcon(),
            Stretch = Stretch.Uniform,
            Width = btn,
            Height = btn
        };
        var play = new Border
        {
            Width = btn,
            Height = btn,
            CornerRadius = new CornerRadius(t.StartTaskButtonCornerRadius),
            ClipToBounds = true,
            Child = playImg,
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 0, 0)
        };
        WeekBoard.ScheduleTip(play, "开始任务");
        play.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            handlers.OnSelect();
            await handlers.OnStart();
        };

        var recImg = new Image
        {
            Source = Theme.LoadRecordIcon(),
            Stretch = Stretch.Uniform,
            Width = btn,
            Height = btn
        };
        var record = new Border
        {
            Width = btn,
            Height = btn,
            Background = Brushes.Transparent,
            Child = recImg,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 2, 0)
        };
        WeekBoard.ScheduleTip(record, "记录任务");
        record.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            handlers.OnSelect();
            await handlers.OnRecord();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0)
        };
        buttons.Children.Add(record);
        buttons.Children.Add(play);

        var overlay = new Grid { Height = thumbH };
        if (task.AllowOverflow)
        {
            var required = SessionLogic.RequiredSeconds(task.RewardMinutes);
            var fill = required <= 0 ? 0 : Math.Clamp(task.OverflowSeconds / required, 0, 1);
            var barH = thumbH / 2;
            var tip = $"溢出进度累计：{Math.Round(task.OverflowSeconds / 60)}/{task.RewardMinutes} min";
            var fillBar = new Border
            {
                Height = Math.Max(0, fill * barH),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Theme.Brush("OverflowFillBrush"),
                CornerRadius = new CornerRadius(3)
            };
            var barHost = new Grid
            {
                Width = 6,
                Height = barH,
                Margin = new Thickness(5, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            WeekBoard.ScheduleTip(barHost, tip);
            barHost.Children.Add(new Border
            {
                Background = Theme.Brush("OverflowTrackBrush"),
                CornerRadius = new CornerRadius(3)
            });
            barHost.Children.Add(fillBar);
            overlay.Children.Add(barHost);
        }
        overlay.Children.Add(buttons);

        var caption =
            $"{task.Title}\n{task.TypeLabel} · {task.LevelLabel}" +
            (string.IsNullOrWhiteSpace(task.PhaseLabel) ? "" : " · " + task.PhaseLabel) +
            (string.IsNullOrWhiteSpace(task.Status) ? "" : "\n" + task.Status);

        return ThumbCard.Build(new ThumbCard.Options
        {
            Width = w,
            Height = t.ScheduleCardHeight,
            ThumbHeight = thumbH,
            CornerRadius = t.CardCornerRadius,
            Preview = task.Preview,
            ThumbBackground = task.Preview is null
                ? BlockPatterns.CreateBrush(task.ResolveStyle(), w, thumbH)
                : Brushes.Transparent,
            ThumbOverlay = overlay,
            Caption = caption,
            CardBackground = new SolidColorBrush(Color.FromArgb(40, task.CardColor.R, task.CardColor.G, task.CardColor.B)),
            BorderBrush = handlers.Selected ? Theme.Brush("AccentBrush") : TaskVisual.BrushOf(task.ColorHex),
            BorderThickness = handlers.Selected ? 3 : 1,
            Opacity = task.Archived ? 0.55 : 1,
            OnSelect = handlers.OnSelect,
            OnDoubleClick = handlers.OnEdit
        });
    }
}
