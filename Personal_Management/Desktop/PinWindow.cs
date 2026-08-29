using System.Windows;
using System.Windows.Controls;

namespace PersonalManagement.Desktop;

internal static class PinWindow
{
    public static string? Prompt(Window owner, string title, bool confirmTwice = false)
    {
        var box = new PasswordBox { Margin = new Thickness(0, 8, 0, 8), Width = 240 };
        var box2 = new PasswordBox { Margin = new Thickness(0, 8, 0, 8), Width = 240 };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = confirmTwice ? "设置二级密码（查看私密 tag 时用）" : "输入二级密码以显示私密收藏" });
        panel.Children.Add(box);
        if (confirmTwice)
        {
            panel.Children.Add(new TextBlock { Text = "再输一次" });
            panel.Children.Add(box2);
        }
        var ok = new Button { Content = "确定", Width = 80, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(ok);

        var win = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner
        };
        Theme.Tint(win);
        string? result = null;
        ok.Click += (_, _) =>
        {
            if (confirmTwice && box.Password != box2.Password)
            {
                MessageBox.Show("两次密码不一致。");
                return;
            }
            if (string.IsNullOrWhiteSpace(box.Password) || box.Password.Length < 4)
            {
                MessageBox.Show("至少 4 位。");
                return;
            }
            result = box.Password;
            win.DialogResult = true;
            win.Close();
        };
        return win.ShowDialog() == true ? result : null;
    }
}
