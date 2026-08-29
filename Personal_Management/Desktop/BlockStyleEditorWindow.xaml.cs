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
    private Slider? _opacity, _thickness, _spacing, _angle, _size, _offX, _offY;
    private ComboBox? _kindBox;
    private TextBlock? _paramHint;

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

        _opacity = AddSlider("透明度", 0, 1, 0.01, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Opacity = v;
            RefreshPreview();
        });
        _thickness = AddSlider("粗细", 0.5, 32, 0.1, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Thickness = v;
            RefreshPreview();
        });
        _spacing = AddSlider("间隔", 2, 64, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Spacing = v;
            RefreshPreview();
        });
        _angle = AddSlider("角度", 0, 360, 1, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Angle = v;
            RefreshPreview();
        });
        _size = AddSlider("图案尺寸", 0.5, 32, 0.1, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].Size = v;
            RefreshPreview();
        });
        _offX = AddSlider("X 重复偏移", -40, 40, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].OffsetX = v;
            RefreshPreview();
        });
        _offY = AddSlider("Y 重复偏移", -40, 40, 0.5, v =>
        {
            if (_selected >= 0) _spec.Layers[_selected].OffsetY = v;
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

    private Slider AddSlider(string label, double min, double max, double tick, Action<double> onChange)
    {
        ParamHost.Children.Add(RowLabel(label));
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var val = new TextBlock { Width = 40, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        DockPanel.SetDock(val, Dock.Right);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = tick,
            IsSnapToTickEnabled = tick >= 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, _) =>
        {
            if (_building) return;
            val.Text = slider.Value.ToString(tick < 1 ? "0.##" : "0", CultureInfo.InvariantCulture);
            onChange(slider.Value);
        };
        row.Children.Add(val);
        row.Children.Add(slider);
        ParamHost.Children.Add(row);
        return slider;
    }

    private void ClearParamEditors()
    {
        _building = true;
        if (_kindBox is not null) _kindBox.IsEnabled = false;
        SetSlidersEnabled(false);
        if (_paramHint is not null)
            _paramHint.Text = "尚未添加纹样层。可只改底色，或点「添加层」。";
        _building = false;
    }

    private void SetSlidersEnabled(bool on)
    {
        foreach (var s in new[] { _opacity, _thickness, _spacing, _angle, _size, _offX, _offY })
            if (s is not null) s.IsEnabled = on;
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
        SetSlidersEnabled(true);
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
        SetSlider(_opacity, l.Opacity);
        SetSlider(_thickness, l.Thickness);
        SetSlider(_spacing, l.Spacing);
        SetSlider(_angle, l.Angle);
        SetSlider(_size, l.Size);
        SetSlider(_offX, l.OffsetX);
        SetSlider(_offY, l.OffsetY);
        _building = false;
        UpdateParamHint();
    }

    private static void SetSlider(Slider? s, double v)
    {
        if (s is null) return;
        s.Value = Math.Clamp(v, s.Minimum, s.Maximum);
    }

    private void UpdateParamHint()
    {
        if (_paramHint is null || _selected < 0) return;
        var k = _spec.Layers[_selected].Kind;
        _paramHint.Text = k switch
        {
            "stripe" => "斜纹：主要用粗细、间隔、角度；尺寸一般不用。",
            "sine" => "正弦纹路：粗细=线宽，间隔=波长，尺寸=振幅，可调角度。",
            "diamond" or "star" or "dot" or "moon" => "散布：尺寸=图案大小，间隔=铺贴周期；角度旋转整平铺；偏移移动格子。",
            _ => "统一参数：透明度、间隔、角度、粗细、尺寸、XY 偏移；不同类型侧重不同字段。"
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
