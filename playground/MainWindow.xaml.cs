using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Playground;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WaveRow> _waves = [];
    private WaveOutEvent? _output;
    private bool _suppressSave;

    public IReadOnlyList<WaveTypeOption> WaveTypes { get; } =
    [
        new("正弦", SignalGeneratorType.Sin),
        new("方波", SignalGeneratorType.Square),
        new("锯齿", SignalGeneratorType.SawTooth),
        new("三角", SignalGeneratorType.Triangle),
        new("白噪声", SignalGeneratorType.White),
        new("粉红噪声", SignalGeneratorType.Pink)
    ];

    public MainWindow()
    {
        DataContext = this;
        _suppressSave = true;
        InitializeComponent();
        WaveList.ItemsSource = _waves;
        LoadSettings();
        _suppressSave = false;
        _waves.CollectionChanged += Waves_CollectionChanged;
        Closed += (_, _) =>
        {
            StopPlayback();
            SaveSettings();
        };
    }

    private void LoadSettings()
    {
        _suppressSave = true;
        var saved = SettingsStore.Load();
        DurationBox.Text = saved.DurationSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        _waves.Clear();
        foreach (var w in saved.Waves)
        {
            Enum.TryParse<SignalGeneratorType>(w.Type, true, out var type);
            if (!Enum.IsDefined(type)) type = SignalGeneratorType.Sin;
            _waves.Add(BindRow(new WaveRow
            {
                Type = type,
                Frequency = w.Frequency,
                Gain = w.Gain
            }));
        }
        if (_waves.Count == 0)
            _waves.Add(BindRow(new WaveRow()));
        _suppressSave = false;
        StatusText.Text = File.Exists(SettingsStore.FilePath)
            ? "已载入上次设置"
            : "还没有上次设置，用默认正弦。";
    }

    private WaveRow BindRow(WaveRow row)
    {
        row.PropertyChanged += Wave_PropertyChanged;
        return row;
    }

    private void Waves_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WaveRow row in e.OldItems)
                row.PropertyChanged -= Wave_PropertyChanged;
        }
        SaveSettings();
    }

    private void Wave_PropertyChanged(object? sender, PropertyChangedEventArgs e) => SaveSettings();

    private void DurationBox_TextChanged(object sender, TextChangedEventArgs e) => SaveSettings();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var last = _waves.LastOrDefault();
        _waves.Add(BindRow(new WaveRow
        {
            Type = last?.Type ?? SignalGeneratorType.Sin,
            Frequency = last is null ? 440 : Math.Min(12000, last.Frequency * 2),
            Gain = last?.Gain ?? 0.12
        }));
        StatusText.Text = $"共 {_waves.Count} 个波形";
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_waves.Count <= 1)
        {
            StatusText.Text = "至少留一个波形。";
            return;
        }
        if ((sender as FrameworkElement)?.Tag is WaveRow row)
        {
            _waves.Remove(row);
            StatusText.Text = $"共 {_waves.Count} 个波形";
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(DurationBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && !double.TryParse(DurationBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out seconds))
        {
            MessageBox.Show("时长要填数字。");
            return;
        }
        seconds = Math.Clamp(seconds, 0.05, 30);

        try
        {
            StopPlayback();
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
            {
                ReadFully = true
            };
            foreach (var row in _waves)
            {
                var gain = Math.Clamp(row.Gain, 0, 1);
                var freq = Math.Clamp(row.Frequency, 1, 12000);
                mixer.AddMixerInput(new SignalGenerator(44100, 2)
                {
                    Type = row.Type,
                    Frequency = (float)freq,
                    Gain = (float)gain
                });
            }

            _output = new WaveOutEvent();
            _output.PlaybackStopped += (_, _) => Dispatcher.Invoke(() =>
            {
                StatusText.Text = "已停止";
            });
            _output.Init(mixer.Take(TimeSpan.FromSeconds(seconds)));
            _output.Play();
            StatusText.Text = $"播放 {_waves.Count} 路混音…";
            SaveSettings();
        }
        catch (Exception ex)
        {
            StopPlayback();
            MessageBox.Show(ex.Message, "播放失败");
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        StatusText.Text = "已停止";
    }

    private void StopPlayback()
    {
        if (_output is null) return;
        try { _output.Stop(); } catch { /* ignore */ }
        _output.Dispose();
        _output = null;
    }

    private void SaveSettings()
    {
        if (_suppressSave) return;
        if (!double.TryParse(DurationBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && !double.TryParse(DurationBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out seconds))
            seconds = 1.2;
        var saved = new SavedSettings
        {
            DurationSeconds = Math.Clamp(seconds, 0.05, 30),
            Waves = _waves.Select(w => new SavedWave
            {
                Type = w.Type.ToString(),
                Frequency = w.Frequency,
                Gain = w.Gain
            }).ToList()
        };
        if (saved.Waves.Count == 0)
            saved.Waves.Add(new SavedWave());
        try { SettingsStore.Save(saved); }
        catch { /* ignore disk errors while typing */ }
    }
}
