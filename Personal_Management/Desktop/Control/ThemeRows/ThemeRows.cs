using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PersonalManagement.Desktop;

/// <summary>界面页主题参数行：色值 / 数值。临时编辑态，提交由调用方负责。</summary>
internal static class ThemeRows
{
    public static UIElement ColorRow(
        string label,
        string hex,
        Action<string> set,
        Func<bool> isBuilding,
        Func<bool> guardEditable,
        Action commit)
    {
        var currentHex = hex;
        var swatch = new Border
        {
            Width = 36,
            Height = 22,
            Background = Theme.FromHex(hex),
            BorderBrush = Theme.Brush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var box = new TextBox { Text = hex, Width = 90, VerticalAlignment = VerticalAlignment.Center };
        var pick = new Button { Content = "取色…" };
        pick.Click += (_, _) =>
        {
            if (!guardEditable()) return;
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
            var c = TaskVisual.ParseColor(currentHex);
            dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            ApplyColor($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
        };
        box.LostFocus += (_, _) => ApplyColor(box.Text.Trim());
        void ApplyColor(string value)
        {
            if (isBuilding()) return;
            if (!guardEditable()) { box.Text = currentHex; return; }
            set(value);
            currentHex = value;
            box.Text = value;
            swatch.Background = Theme.FromHex(value);
            commit();
        }
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(swatch);
        right.Children.Add(box);
        right.Children.Add(pick);
        row.Children.Add(right);
        return row;
    }

    public static UIElement NumberRow(
        string label,
        double value,
        double min,
        double max,
        Action<double> set,
        Func<bool> isBuilding,
        Func<bool> guardEditable,
        Action commit,
        bool integer = false)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center
        };
        var box = new TextBox
        {
            Text = integer ? ((int)Math.Round(value)).ToString() : value.ToString("0"),
            Width = 56,
            Margin = new Thickness(8, 0, 0, 0)
        };
        slider.ValueChanged += (_, _) =>
        {
            if (isBuilding()) return;
            if (!guardEditable()) return;
            var v = integer ? Math.Round(slider.Value) : Math.Round(slider.Value, 1);
            box.Text = integer ? ((int)v).ToString() : v.ToString("0.#");
            set(v);
            commit();
        };
        box.LostFocus += (_, _) =>
        {
            if (isBuilding()) return;
            if (!double.TryParse(box.Text.Trim(), out var parsed)) return;
            parsed = Math.Clamp(parsed, min, max);
            slider.Value = parsed;
        };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(slider);
        right.Children.Add(box);
        row.Children.Add(right);
        return row;
    }
}
