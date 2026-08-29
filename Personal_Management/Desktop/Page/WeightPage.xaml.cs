using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PersonalManagement.Desktop;

public partial class WeightPage : UserControl
{
    public WeightPage()
    {
        InitializeComponent();
    }


    private IAppHost _host = null!;

    public void Attach(IAppHost host)
    {
        _host = host;
        if (WeightSexBox is not null && WeightSexBox.Items.Count == 0)
        {
            WeightSexBox.Items.Add(new ComboBoxItem { Content = "男", Tag = "male" });
            WeightSexBox.Items.Add(new ComboBoxItem { Content = "女", Tag = "female" });
            WeightSexBox.SelectedIndex = 0;
        }
        if (WeightActivityBox is not null && WeightActivityBox.Items.Count == 0)
        {
            foreach (var (id, label, _) in WeightLogic.ActivityLevels)
                WeightActivityBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
            if (WeightActivityBox.Items.Count > 0)
                WeightActivityBox.SelectedIndex = 0;
        }

    }

    private string? _weightProfileId;
    private readonly List<(DateTime Date, double Kg)> _weightEntries = [];
    private bool _weightRangeReady;
    private void ClearWeightUi()
    {
        _weightProfileId = null;
        _weightEntries.Clear();
        WeightChartCanvas?.Children.Clear();
        if (WeightStatsText is not null)
            WeightStatsText.Text = "";
    }

    private void EnsureWeightRangeDefaults()
    {
        if (_weightRangeReady) return;
        var today = DateTime.Today;
        if (WeightChartFromBox is not null && string.IsNullOrWhiteSpace(WeightChartFromBox.Text))
            WeightChartFromBox.Text = WeightLogic.FormatDate(today.AddDays(-30));
        if (WeightChartToBox is not null && string.IsNullOrWhiteSpace(WeightChartToBox.Text))
            WeightChartToBox.Text = WeightLogic.FormatDate(today);
        _weightRangeReady = true;
    }

    private static void SelectComboByTag(ComboBox? box, string? tag)
    {
        if (box is null || tag is null) return;
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem cbi && (cbi.Tag as string) == tag)
            {
                box.SelectedItem = cbi;
                return;
            }
        }
    }

    private static string? SelectedComboTag(ComboBox? box) =>
        (box?.SelectedItem as ComboBoxItem)?.Tag as string;

    private async Task LoadWeightAsync()
    {
        if (WeightChartCanvas is null) return;
        EnsureWeightRangeDefaults();
        var profiles = await _host.Session.Weight.ListRecordsAsync(StoreTables.WeightProfile);
        var profile = profiles.FirstOrDefault();
        _weightProfileId = NocoClient.ReadId(profile);

        var height = NocoClient.ReadDouble(profile, "HeightCm", 170);
        var age = NocoClient.ReadInt(profile, "AgeYears", 30);
        var sex = NocoClient.ReadString(profile, "Sex") ?? "male";
        var activity = NocoClient.ReadString(profile, "Activity") ?? "sedentary";

        if (WeightHeightBox is not null)
            WeightHeightBox.Text = height.ToString("0.##");
        if (WeightAgeBox is not null)
            WeightAgeBox.Text = age.ToString();
        SelectComboByTag(WeightSexBox, sex);
        SelectComboByTag(WeightActivityBox, activity);

        var entries = await _host.Session.Weight.ListRecordsAsync(StoreTables.WeightEntries);
        _weightEntries.Clear();
        foreach (var n in entries)
        {
            var date = WeightLogic.ParseEntryDate(n);
            if (date is null) continue;
            _weightEntries.Add((date.Value, NocoClient.ReadDouble(n, "WeightKg")));
        }
        _weightEntries.Sort((a, b) => a.Date.CompareTo(b.Date));

        var latest = _weightEntries.Count > 0 ? _weightEntries[^1].Kg : (double?)null;
        UpdateWeightStatsText(latest, height, age, sex, activity);
        RenderWeightChart();
    }

    private void WeightChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderWeightChart();

    private void WeightChartLast30_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        if (WeightChartFromBox is not null)
            WeightChartFromBox.Text = WeightLogic.FormatDate(today.AddDays(-30));
        if (WeightChartToBox is not null)
            WeightChartToBox.Text = WeightLogic.FormatDate(today);
        RenderWeightChart();
    }

    private void WeightChartRange_Changed(object sender, RoutedEventArgs e) =>
        RenderWeightChart();

    private bool TryReadWeightChartRange(out DateTime from, out DateTime to, out string? error)
    {
        from = default;
        to = default;
        error = null;
        EnsureWeightRangeDefaults();
        var fromRaw = (WeightChartFromBox?.Text ?? "").Trim();
        var toRaw = (WeightChartToBox?.Text ?? "").Trim();
        if (!DateTime.TryParseExact(fromRaw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out from))
        {
            error = "起始日期请用 yyyy-MM-dd。";
            return false;
        }
        if (!DateTime.TryParseExact(toRaw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out to))
        {
            error = "结束日期请用 yyyy-MM-dd。";
            return false;
        }
        from = from.Date;
        to = to.Date;
        if (to < from)
        {
            error = "结束日期不能早于起始日期。";
            return false;
        }
        return true;
    }

    private void RenderWeightChart()
    {
        if (WeightChartCanvas is null) return;
        var t = Theme.Current;
        WeightChartCanvas.Height = t.WeightChartHeight;
        WeightChartCanvas.Children.Clear();

        var w = WeightChartCanvas.ActualWidth;
        var h = WeightChartCanvas.ActualHeight;
        if (w < 40 || h < 40) return;

        var plotBg = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            Fill = Theme.FromHex(t.WeightChartPlotBackground)
        };
        WeightChartCanvas.Children.Add(plotBg);

        if (!TryReadWeightChartRange(out var from, out var to, out _))
        {
            AddChartHint(w, h, "横向日期格式无效（yyyy-MM-dd）。");
            return;
        }

        var spanDays = Math.Max(1, (to - from).TotalDays);
        var inRange = _weightEntries.Where(e => e.Date >= from && e.Date <= to).OrderBy(e => e.Date).ToList();
        // 纵轴固定按全部历史数据，不随横向显示范围伸缩
        var histMax = _weightEntries.Count > 0 ? _weightEntries.Max(e => e.Kg) : 80;
        var histMin = _weightEntries.Count > 0 ? _weightEntries.Min(e => e.Kg) : 40;
        var yMax = Math.Ceiling(histMax + 2);
        var yMin = Math.Floor(histMin - 2);
        if (yMax <= yMin) yMax = yMin + 1;
        var ySpan = yMax - yMin;
        const double padL = 44, padR = 16, padT = 16, padB = 28;
        var plotW = Math.Max(1, w - padL - padR);
        var plotH = Math.Max(1, h - padT - padB);

        var gridBrush = Theme.FromHex(t.WeightChartGrid);
        var textBrush = Theme.Brush("TextSecondaryBrush");
        for (var kg = (int)yMin; kg <= (int)yMax; kg++)
        {
            var y = padT + plotH * (1 - (kg - yMin) / ySpan);
            var grid = new Line
            {
                X1 = padL,
                X2 = padL + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            WeightChartCanvas.Children.Add(grid);
            var label = new TextBlock
            {
                Text = kg.ToString(),
                FontSize = 11,
                Foreground = textBrush
            };
            WeightChartCanvas.Children.Add(label);
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 8);
        }

        var axisX = new Line
        {
            X1 = padL, X2 = padL + plotW, Y1 = padT + plotH, Y2 = padT + plotH,
            Stroke = gridBrush, StrokeThickness = 1.5
        };
        var axisY = new Line
        {
            X1 = padL, X2 = padL, Y1 = padT, Y2 = padT + plotH,
            Stroke = gridBrush, StrokeThickness = 1.5
        };
        WeightChartCanvas.Children.Add(axisX);
        WeightChartCanvas.Children.Add(axisY);

        void AddXLabel(DateTime day, double x)
        {
            var label = new TextBlock
            {
                Text = day.ToString("MM-dd"),
                FontSize = 11,
                Foreground = textBrush
            };
            WeightChartCanvas.Children.Add(label);
            Canvas.SetLeft(label, x - 14);
            Canvas.SetTop(label, padT + plotH + 6);
        }
        AddXLabel(from, padL);
        AddXLabel(to, padL + plotW);

        if (inRange.Count == 0)
        {
            AddChartHint(w, h, _weightEntries.Count == 0
                ? "还没有体重记录。点「记今天」或「批量粘贴…」。"
                : "当前横向范围内没有数据点。");
            return;
        }

        double XOf(DateTime d) => padL + plotW * ((d.Date - from).TotalDays / spanDays);
        double YOf(double kg) => padT + plotH * (1 - Math.Clamp((kg - yMin) / ySpan, 0, 1));

        var lineBrush = Theme.FromHex(t.WeightChartLine);
        if (inRange.Count >= 2)
        {
            var poly = new Polyline
            {
                Stroke = lineBrush,
                StrokeThickness = t.WeightChartLineThickness,
                StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var (date, kg) in inRange)
                poly.Points.Add(new Point(XOf(date), YOf(kg)));
            WeightChartCanvas.Children.Add(poly);
        }

        var r = Math.Max(1, t.WeightChartPointSize / 2);
        foreach (var (date, kg) in inRange)
        {
            var cx = XOf(date);
            var cy = YOf(kg);
            var dot = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = lineBrush,
                ToolTip = $"{WeightLogic.FormatDate(date)}  {kg:0.##} kg"
            };
            WeightChartCanvas.Children.Add(dot);
            Canvas.SetLeft(dot, cx - r);
            Canvas.SetTop(dot, cy - r);
        }
    }

    private void AddChartHint(double w, double h, string text)
    {
        if (WeightChartCanvas is null) return;
        var hint = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Width = Math.Max(80, w - 48),
            Foreground = Theme.Brush("TextSecondaryBrush"),
            TextAlignment = TextAlignment.Center
        };
        WeightChartCanvas.Children.Add(hint);
        Canvas.SetLeft(hint, 24);
        Canvas.SetTop(hint, Math.Max(24, h / 2 - 20));
    }

    private void UpdateWeightStatsText(double? latestKg, double heightCm, int ageYears, string sex, string activity)
    {
        if (WeightStatsText is null) return;
        if (latestKg is null || latestKg <= 0)
        {
            WeightStatsText.Text = "填写档案并记录体重后显示 BMI / BMR / TDEE。";
            return;
        }
        var bmi = WeightLogic.Bmi(latestKg.Value, heightCm);
        var bmr = WeightLogic.Bmr(latestKg.Value, heightCm, ageYears, sex);
        var tdee = WeightLogic.Tdee(bmr, activity);
        WeightStatsText.Text =
            $"最近体重 {latestKg.Value:0.##} kg · BMI {(bmi is null ? "—" : bmi.Value.ToString("0.##"))}（{WeightLogic.BmiCategory(bmi)}）" +
            $" · BMR {(bmr is null ? "—" : bmr.Value.ToString("0"))} · TDEE {(tdee is null ? "—" : tdee.Value.ToString("0"))}";
    }

    private bool TryReadWeightProfileFromUi(out double heightCm, out int ageYears, out string sex, out string activity, out string? error)
    {
        heightCm = 0;
        ageYears = 0;
        sex = SelectedComboTag(WeightSexBox) ?? "male";
        activity = SelectedComboTag(WeightActivityBox) ?? "sedentary";
        error = null;
        if (!double.TryParse((WeightHeightBox?.Text ?? "").Trim(), out heightCm) || heightCm < 50 || heightCm > 300)
        {
            error = "身高请填 50–300 cm 的数字。";
            return false;
        }
        if (!int.TryParse((WeightAgeBox?.Text ?? "").Trim(), out ageYears) || ageYears < 1 || ageYears > 120)
        {
            error = "年龄请填 1–120 的整数。";
            return false;
        }
        return true;
    }

    private async void WeightSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_host.Session.WeightReady) return;
        if (!TryReadWeightProfileFromUi(out var height, out var age, out var sex, out var activity, out var error))
        {
            MessageBox.Show(error, "体重档案");
            return;
        }
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["HeightCm"] = height,
                ["AgeYears"] = age,
                ["Sex"] = sex,
                ["Activity"] = activity
            };
            if (string.IsNullOrEmpty(_weightProfileId))
            {
                fields["Title"] = "main";
                var created = await _host.Session.Weight.CreateRecordAsync(StoreTables.WeightProfile, fields);
                _weightProfileId = NocoClient.ReadId(created);
            }
            else
            {
                fields["Id"] = _weightProfileId;
                await _host.Session.Weight.PatchRecordAsync(StoreTables.WeightProfile, fields);
            }
            await LoadWeightAsync();
            _host.StatusText = "已保存体重档案";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败");
        }
    }

    private async void WeightToday_Click(object sender, RoutedEventArgs e)
    {
        if (!_host.Session.WeightReady) return;
        var raw = TextPrompt.Ask(_host.OwnerWindow, "记今天体重", "体重（kg）", "");
        if (raw is null) return;
        if (!WeightLogic.TryParseWeight(raw, out var kg, out var werr))
        {
            MessageBox.Show(werr ?? "体重不合法", "记今天");
            return;
        }
        try
        {
            await UpsertWeightEntryAsync(DateTime.Today, kg);
            await LoadWeightAsync();
            _host.StatusText = $"已记录今天体重 {kg:0.##} kg";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "记录失败");
        }
    }

    private async void WeightBatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_host.Session.WeightReady) return;
        var box = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinWidth = 420,
            MinHeight = 220,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 8, 0, 12)
        };
        var ok = new Button { Content = "确定", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "每行：yyyy-MM-dd（或 a）与体重，中间用空格或 Tab 分隔（可粘贴表格；本框可按 Tab 输入制表符）。",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480
        });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = "批量粘贴体重",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = _host.OwnerWindow
        };
        Theme.Tint(win);
        var accepted = false;
        ok.Click += (_, _) => { accepted = true; win.DialogResult = true; };
        if (win.ShowDialog() != true || !accepted) return;
        try
        {
            var rows = WeightLogic.ParseBatch(box.Text);
            foreach (var (date, kg) in rows)
                await UpsertWeightEntryAsync(date, kg);
            await LoadWeightAsync();
            _host.StatusText = $"已导入 {rows.Count} 条体重记录";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "批量导入失败");
        }
    }

    private async Task UpsertWeightEntryAsync(DateTime date, double kg)
    {
        var dateStr = WeightLogic.FormatDate(date.Date);
        var rows = await _host.Session.Weight.ListRecordsAsync(StoreTables.WeightEntries);
        foreach (var n in rows)
        {
            var d = WeightLogic.ParseEntryDate(n);
            if (d != date.Date) continue;
            var id = NocoClient.ReadId(n) ?? throw new InvalidOperationException("体重记录缺少 Id");
            await _host.Session.Weight.PatchRecordAsync(StoreTables.WeightEntries, new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["Date"] = dateStr,
                ["WeightKg"] = kg
            });
            return;
        }
        await _host.Session.Weight.CreateRecordAsync(StoreTables.WeightEntries, new Dictionary<string, object?>
        {
            ["Date"] = dateStr,
            ["WeightKg"] = kg
        });
    }

    private static void SetOfflineOverlay(UIElement? overlay, UIElement? content, bool show)
    {
        if (overlay is null || content is null) return;
        overlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        content.IsEnabled = !show;
        content.Opacity = show ? 0.35 : 1;
    }



    public void UpdateOfflineOverlay()
    {
        SetOfflineOverlay(WeightOfflineOverlay, WeightDock, !_host.Session.WeightReady);
    }

    public async Task ReloadAsync() => await LoadWeightAsync();

    public void ClearUi() => ClearWeightUi();

    public void OnHostThemeChanged()
    {
        if (_host.Session.WeightReady)
            RenderWeightChart();
        else
            ClearWeightUi();
    }

    private async void TryConnectNoco_Click(object sender, RoutedEventArgs e) =>
        await _host.TryConnectNocoAsync();
}
