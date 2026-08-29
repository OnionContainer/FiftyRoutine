using System.Windows;
using System.Windows.Controls;

namespace PersonalManagement.Desktop;

/// <summary>记录任务时长选择：从上次结束起记固定时长，或至今。</summary>
public sealed class RecordDurationWindow : Window
{
    public enum ChoiceKind { Minutes, UntilNow }

    public ChoiceKind Choice { get; private set; } = ChoiceKind.UntilNow;
    public int Minutes { get; private set; }

    public RecordDurationWindow(DateTime lastEnded, DateTime now)
    {
        Title = "记录日程";
        Width = 360;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Theme.Tint(this);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "从上一次任务结束起，记录多长时间？",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var row = new WrapPanel();
        void AddMin(int m, string label)
        {
            var end = lastEnded.AddMinutes(m);
            var btn = new Button
            {
                Content = label,
                Margin = new Thickness(0, 0, 8, 8),
                MinWidth = 72,
                IsEnabled = end <= now
            };
            btn.Click += (_, _) =>
            {
                Choice = ChoiceKind.Minutes;
                Minutes = m;
                DialogResult = true;
                Close();
            };
            row.Children.Add(btn);
        }
        AddMin(10, "10 分钟");
        AddMin(20, "20 分钟");
        AddMin(30, "30 分钟");
        AddMin(60, "1 小时");
        var until = new Button { Content = "至今", Margin = new Thickness(0, 0, 8, 8), MinWidth = 72 };
        until.Click += (_, _) =>
        {
            Choice = ChoiceKind.UntilNow;
            DialogResult = true;
            Close();
        };
        row.Children.Add(until);
        root.Children.Add(row);
        var cancel = new Button { Content = "取消", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        root.Children.Add(cancel);
        Content = root;
    }
}
