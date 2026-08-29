using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PersonalManagement.Desktop;

public partial class BlockStyleEditorWindow : Window
{
    public BlockStyleSpec ResultSpec { get; private set; }

    private readonly BlockStyleSpec _spec;
    private int _selected = -1;
    private enum ColorTarget { Base, Layer }
    private ColorTarget _colorTarget = ColorTarget.Base;
    private bool _building;
    private ParamControl? _opacity, _thickness, _spacing, _angle, _size, _offX, _offY, _cumX, _cumY;
    private ComboBox? _kindBox;
    private TextBlock? _paramHint;

    private sealed class ParamControl
    {
        public required Slider Slider { get; init; }
        public required TextBox Box { get; init; }
        public required double Tick { get; init; }

        public void SetEnabled(bool on)
        {
            Slider.IsEnabled = on;
            Box.IsEnabled = on;
        }

        public void SetValue(double v, bool syncing)
        {
            var clamped = Math.Clamp(v, Slider.Minimum, Slider.Maximum);
            Slider.Value = clamped;
            Box.Text = Format(clamped);
        }

        public string Format(double v) =>
            v.ToString(Tick < 1 ? "0.##" : "0", CultureInfo.InvariantCulture);
    }

    public BlockStyleEditorWindow(BlockStyleSpec? initial = null)
    {
        InitializeComponent();
        Theme.Tint(this);
        _spec = (initial ?? new BlockStyleSpec()).Clone();
        _spec.Normalize();
        ResultSpec = _spec.Clone();
        HlsPicker.ColorChanged += OnHlsColor;
        ReloadPresetBox();
        RebuildLayerList();
        BuildParamEditors();
        SelectColorTarget(ColorTarget.Base);
        RefreshPreview();
        if (_spec.Layers.Count > 0)
        {
            LayerList.SelectedIndex = 0;
        }
        else
            ClearParamEditors();
    }

    private void ReloadPresetBox(string? selectId = null)
    {
        var keep = selectId ?? (PresetBox.SelectedItem as BlockStylePreset)?.Id;
        PresetBox.Items.Clear();
        foreach (var p in BlockStylePresets.Load())
            PresetBox.Items.Add(p);
        if (keep is not null)
        {
            foreach (BlockStylePreset p in PresetBox.Items)
            {
                if (p.Id == keep)
                {
                    PresetBox.SelectedItem = p;
                    return;
                }
            }
        }
        if (PresetBox.Items.Count > 0 && PresetBox.SelectedIndex < 0)
            PresetBox.SelectedIndex = 0;
    }

    private void ApplySpec(BlockStyleSpec source)
    {
        var c = source.Clone();
        c.Normalize();
        _spec.BaseColor = c.BaseColor;
        _spec.Layers = c.Layers;
        RebuildLayerList();
        if (_spec.Layers.Count > 0)
            LayerList.SelectedIndex = 0;
        else
            ClearParamEditors();
        SelectColorTarget(_colorTarget == ColorTarget.Layer && _spec.Layers.Count > 0
            ? ColorTarget.Layer
            : ColorTarget.Base);
        RefreshPreview();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not BlockStylePreset p)
        {
            MessageBox.Show("请先选择一个预设。");
            return;
        }
        ApplySpec(p.Spec);
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        _spec.Normalize();
        var suggested = (PresetBox.SelectedItem as BlockStylePreset)?.Name ?? "我的样式";
        var name = TextPrompt.Ask(this, "存为预设", "预设名称", suggested);
        if (name is null) return;
        var saved = BlockStylePresets.Upsert(name, _spec);
        ReloadPresetBox(saved.Id);
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not BlockStylePreset p)
        {
            MessageBox.Show("请先选择要删除的预设。");
            return;
        }
        var ok = MessageBox.Show($"删除预设「{p.Name}」？", "个人管理",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        BlockStylePresets.Delete(p.Id);
        ReloadPresetBox();
    }

    /// <summary>独立运行入口（Probes / 命令行）。</summary>
    public static void RunStandalone(BlockStyleSpec? initial = null)
    {
        var createdApp = false;
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            createdApp = true;
        }
        Theme.LoadAndApply();
        var win = new BlockStyleEditorWindow(initial);
        win.ShowDialog();
        if (createdApp)
            Application.Current?.Shutdown();
    }

    private void RebuildLayerList()
    {
        _building = true;
        var keep = LayerList.SelectedIndex;
        LayerList.Items.Clear();
        for (var i = 0; i < _spec.Layers.Count; i++)
        {
            var l = _spec.Layers[i];
            var label = BlockStyleLayer.Kinds.FirstOrDefault(k => k.Id == l.Kind).Label;
            if (string.IsNullOrEmpty(label)) label = l.Kind;
            LayerList.Items.Add($"#{i + 1} {label}");
        }
        _building = false;
        if (_spec.Layers.Count == 0)
        {
            _selected = -1;
            ClearParamEditors();
        }
        else
        {
            LayerList.SelectedIndex = Math.Clamp(keep, 0, _spec.Layers.Count - 1);
        }
    }

    private void BuildParamEditors()
    {
        ParamHost.Children.Clear();
        ParamHost.Children.Add(RowLabel("类型"));
        _kindBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (id, label) in BlockStyleLayer.Kinds)
            _kindBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        _kindBox.SelectionChanged += (_, _) =>
        {
            if (_building || _selected < 0 || _kindBox.SelectedItem is not ComboBoxItem ci) return;
            _spec.Layers[_selected].Kind = BlockStyleLayer.NormalizeKind(ci.Tag as string);
            RebuildLayerList();
            LayerList.SelectedIndex = _selected;
            RefreshPreview();
            UpdateParamHint();
        };
        ParamHost.Children.Add(_kindBox);

        _opacity = AddParam("透明度", 0, 1, 0.01, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Opacity = v;
            RefreshPreview();
        });
        _thickness = AddParam("粗细", 0.5, 32, 0.1, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Thickness = v;
            RefreshPreview();
        });
        _spacing = AddParam("间隔", 2, 64, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Spacing = v;
            RefreshPreview();
        });
        _angle = AddParam("角度", 0, 360, 1, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Angle = v;
            RefreshPreview();
        });
        _size = AddParam("图案尺寸", 0.5, 32, 0.1, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Size = v;
            RefreshPreview();
        });
        _offX = AddParam("X 偏移", -40, 40, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].OffsetX = v;
            RefreshPreview();
        });
        _offY = AddParam("Y 偏移", -40, 40, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].OffsetY = v;
            RefreshPreview();
        });
        _cumX = AddParam("X 累积偏移", -40, 40, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].CumulativeOffsetX = v;
            RefreshPreview();
        });
        _cumY = AddParam("Y 累积偏移", -40, 40, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].CumulativeOffsetY = v;
            RefreshPreview();
        });

        _paramHint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.Brush("TextSecondaryBrush"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 12
        };
        ParamHost.Children.Add(_paramHint);
    }

    private static TextBlock RowLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 4, 0, 2),
        Foreground = Theme.Brush("TextSecondaryBrush")
    };

    private ParamControl AddParam(string label, double min, double max, double tick, Action<double> onChange)
    {
        ParamHost.Children.Add(RowLabel(label));
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var box = new TextBox
        {
            Width = 52,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(box, Dock.Right);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = tick,
            IsSnapToTickEnabled = tick >= 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        var ctrl = new ParamControl { Slider = slider, Box = box, Tick = tick };
        slider.ValueChanged += (_, _) =>
        {
            if (_building) return;
            box.Text = ctrl.Format(slider.Value);
            onChange(slider.Value);
        };
        box.LostFocus += (_, _) =>
        {
            if (_building) return;
            if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && !double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            {
                box.Text = ctrl.Format(slider.Value);
                return;
            }
            v = Math.Clamp(v, min, max);
            _building = true;
            slider.Value = v;
            box.Text = ctrl.Format(v);
            _building = false;
            onChange(v);
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                box.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
                e.Handled = true;
            }
        };
        row.Children.Add(box);
        row.Children.Add(slider);
        ParamHost.Children.Add(row);
        return ctrl;
    }

    private void ClearParamEditors()
    {
        _building = true;
        if (_kindBox is not null) _kindBox.IsEnabled = false;
        SetParamsEnabled(false);
        if (_paramHint is not null)
            _paramHint.Text = "尚未添加纹样层。可只改底色，或点「添加层」。";
        _building = false;
    }

    private void SetParamsEnabled(bool on)
    {
        foreach (var p in new[] { _opacity, _thickness, _spacing, _angle, _size, _offX, _offY, _cumX, _cumY })
            p?.SetEnabled(on);
        if (_kindBox is not null) _kindBox.IsEnabled = on;
    }

    private void LoadParamsFromLayer()
    {
        if (_selected < 0 || _selected >= _spec.Layers.Count)
        {
            ClearParamEditors();
            return;
        }
        var l = _spec.Layers[_selected];
        l.Normalize();
        _building = true;
        SetParamsEnabled(true);
        if (_kindBox is not null)
        {
            foreach (ComboBoxItem item in _kindBox.Items)
            {
                if ((item.Tag as string) == l.Kind)
                {
                    _kindBox.SelectedItem = item;
                    break;
                }
            }
        }
        _opacity?.SetValue(l.Opacity, true);
        _thickness?.SetValue(l.Thickness, true);
        _spacing?.SetValue(l.Spacing, true);
        _angle?.SetValue(l.Angle, true);
        _size?.SetValue(l.Size, true);
        _offX?.SetValue(l.OffsetX, true);
        _offY?.SetValue(l.OffsetY, true);
        _cumX?.SetValue(l.CumulativeOffsetX, true);
        _cumY?.SetValue(l.CumulativeOffsetY, true);
        _building = false;
        UpdateParamHint();
    }

    private void UpdateParamHint()
    {
        if (_paramHint is null || _selected < 0) return;
        var k = _spec.Layers[_selected].Kind;
        _paramHint.Text = k switch
        {
            "stripe" => "斜纹：主要用粗细、间隔、角度；尺寸一般不用。偏移=相位；累积偏移=行间错开。",
            "sine" => "正弦纹路：粗细=线宽，间隔=波长，尺寸=振幅。偏移=相位；累积偏移=行间错开。",
            "diamond" or "star" or "dot" or "moon" => "散布：尺寸=图案大小，间隔=铺贴周期。偏移移动格子；累积偏移使行/列递增错开。",
            _ => "统一参数：透明度、间隔、角度、粗细、尺寸、XY 偏移与累积偏移。"
        };
    }

    private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_building) return;
        _selected = LayerList.SelectedIndex;
        LoadParamsFromLayer();
        if (_colorTarget == ColorTarget.Layer)
            SyncPickerToTarget();
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        _spec.Layers.Add(new BlockStyleLayer());
        RebuildLayerList();
        LayerList.SelectedIndex = _spec.Layers.Count - 1;
        RefreshPreview();
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_selected < 0 || _selected >= _spec.Layers.Count) return;
        _spec.Layers.RemoveAt(_selected);
        RebuildLayerList();
        RefreshPreview();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (_selected <= 0) return;
        (_spec.Layers[_selected - 1], _spec.Layers[_selected]) = (_spec.Layers[_selected], _spec.Layers[_selected - 1]);
        var i = _selected - 1;
        RebuildLayerList();
        LayerList.SelectedIndex = i;
        RefreshPreview();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (_selected < 0 || _selected >= _spec.Layers.Count - 1) return;
        (_spec.Layers[_selected + 1], _spec.Layers[_selected]) = (_spec.Layers[_selected], _spec.Layers[_selected + 1]);
        var i = _selected + 1;
        RebuildLayerList();
        LayerList.SelectedIndex = i;
        RefreshPreview();
    }

    private void TargetBase_Click(object sender, RoutedEventArgs e) => SelectColorTarget(ColorTarget.Base);

    private void TargetLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_selected < 0)
        {
            MessageBox.Show("请先添加并选中一层纹样。");
            return;
        }
        SelectColorTarget(ColorTarget.Layer);
    }

    private void SelectColorTarget(ColorTarget t)
    {
        _colorTarget = t;
        TargetBaseBtn.FontWeight = t == ColorTarget.Base ? FontWeights.Bold : FontWeights.Normal;
        TargetLayerBtn.FontWeight = t == ColorTarget.Layer ? FontWeights.Bold : FontWeights.Normal;
        SyncPickerToTarget();
    }

    private void SyncPickerToTarget()
    {
        var hex = _colorTarget == ColorTarget.Base
            ? _spec.BaseColor
            : (_selected >= 0 ? _spec.Layers[_selected].Color : BlockPatterns.DefaultPatternColor);
        var c = TaskVisual.ParseColor(hex);
        HlsPicker.SelectedColor = c;
        ActiveSwatch.Background = new SolidColorBrush(c);
    }

    private void OnHlsColor(Color c)
    {
        var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (_colorTarget == ColorTarget.Base)
            _spec.BaseColor = hex;
        else if (_selected >= 0)
            _spec.Layers[_selected].Color = hex;
        ActiveSwatch.Background = new SolidColorBrush(c);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        _spec.Normalize();
        PreviewHost.Child = BlockPatterns.BuildVisual(_spec, Math.Max(120, PreviewHost.ActualWidth > 0 ? PreviewHost.ActualWidth : 280), 120);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _spec.Normalize();
        ResultSpec = _spec.Clone();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
