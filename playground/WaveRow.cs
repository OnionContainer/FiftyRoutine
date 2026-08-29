using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.Wave.SampleProviders;

namespace Playground;

internal sealed class WaveRow : INotifyPropertyChanged
{
    private SignalGeneratorType _type = SignalGeneratorType.Sin;
    private double _frequency = 440;
    private double _gain = 0.15;

    public SignalGeneratorType Type
    {
        get => _type;
        set
        {
            if (_type == value) return;
            _type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrequencyEnabled));
        }
    }

    public double Frequency
    {
        get => _frequency;
        set
        {
            if (Math.Abs(_frequency - value) < 0.0001) return;
            _frequency = value;
            OnPropertyChanged();
        }
    }

    public double Gain
    {
        get => _gain;
        set
        {
            if (Math.Abs(_gain - value) < 0.0001) return;
            _gain = value;
            OnPropertyChanged();
        }
    }

    public bool FrequencyEnabled => Type is SignalGeneratorType.Sin
        or SignalGeneratorType.Square
        or SignalGeneratorType.SawTooth
        or SignalGeneratorType.Triangle;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class WaveTypeOption
{
    public string Label { get; }
    public SignalGeneratorType Type { get; }

    public WaveTypeOption(string label, SignalGeneratorType type)
    {
        Label = label;
        Type = type;
    }
}
