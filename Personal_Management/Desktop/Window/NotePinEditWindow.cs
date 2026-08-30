using System.Windows;
using System.Windows.Controls;

namespace PersonalManagement.Desktop;

/// <summary>笔记钉编辑：正文；可删除。时间与列百分比只读展示。</summary>
public sealed class NotePinEditWindow : Window
{
    public string Body { get; private set; } = "";
    public bool DeleteRequested { get; private set; }

    public NotePinEditWindow(DateTime at, double dayColumnPercent, string? body = null, bool isEdit = false)
    {
        Title = isEdit ? "编辑笔记钉" : "新建笔记钉";
        Width = 420;
        Height = 320;
        MinWidth = 360;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Theme.Tint(this);

        var bodyBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 140,
            Text = body ?? ""
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        if (isEdit)
        {
            var del = new Button { Content = "删除", Width = 72, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            del.Click += (_, _) =>
            {
                var ok = MessageBox.Show(this, "删除这条笔记钉？", "个人管理",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ok != MessageBoxResult.Yes) return;
                DeleteRequested = true;
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(del);
        }

        var cancel = new Button { Content = "取消", Width = 72, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var save = new Button { Content = "保存", Width = 72, Height = 32, IsDefault = true };
        save.Click += (_, _) =>
        {
            var text = bodyBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "请输入笔记内容。");
                return;
            }
            Body = text;
            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        var form = new StackPanel();
        form.Children.Add(new TextBlock
        {
            Text = $"{at:yyyy-MM-dd HH:mm:ss} · 列内 {dayColumnPercent * 100:0.#}%",
            Foreground = Theme.Brush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        form.Children.Add(new TextBlock
        {
            Text = "笔记",
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = Theme.Brush("TextSecondaryBrush")
        });
        form.Children.Add(bodyBox);
        root.Children.Add(form);
        Content = root;
        Loaded += (_, _) =>
        {
            bodyBox.Focus();
            bodyBox.CaretIndex = bodyBox.Text?.Length ?? 0;
        };
    }
}
