using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using nkast.Aether.Physics2D;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Dynamics.Contacts;
using AetherVector2 = nkast.Aether.Physics2D.Common.Vector2;

namespace Playground.PiggyBank;

public partial class MainWindow : Window
{
    private const float Ppm = 80f;
    private const float CoinW = 0.18f;
    private const float CoinH = 0.06f;
    private const float CoinDensity = 2.2f;
    private const float CoinFriction = 0.35f;

    private readonly World _world = new(new AetherVector2(0f, 18f));
    private readonly List<CoinVisual> _coins = new();
    private readonly Random _rng = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _saveDebounce;
    private PiggyBankSettings _cfg = new();
    private SolverIterations _solver;

    private Body? _jarFloor;
    private Body? _jarLeft;
    private Body? _jarRight;
    private Rectangle? _jarDraw;
    private bool _jarBuilt;
    private bool _uiReady;
    private DateTime _lastTick = DateTime.UtcNow;

    private float _jarLeftX;
    private float _jarRightX;
    private float _jarBottomY;
    private float _jarTopY;
    private float _wallT = 0.08f;

    public MainWindow()
    {
        InitializeComponent();
        _cfg = PiggyBankSettings.Load();
        _solver = new SolverIterations
        {
            VelocityIterations = _cfg.VelocityIterations,
            PositionIterations = _cfg.PositionIterations,
            TOIVelocityIterations = Settings.TOIVelocityIterations,
            TOIPositionIterations = Settings.TOIPositionIterations
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
        _timer.Tick += (_, _) => Tick();
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            PersistSettings();
        };

        Loaded += (_, _) =>
        {
            WriteParamsToUi();
            ApplySolverSettings();
            _uiReady = true;
            RebuildJar();
            _lastTick = DateTime.UtcNow;
            _timer.Start();
            UpdateStatus();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _saveDebounce.Stop();
            ReadParamsFromUi();
            PersistSettings();
        };
    }

    private void SaveParams_Click(object sender, RoutedEventArgs e)
    {
        ReadParamsFromUi();
        PersistSettings();
        StatusText.Text = "参数已保存 → " + System.IO.Path.GetFileName(PiggyBankSettings.FilePath);
    }

    private void PersistSettings()
    {
        try
        {
            _cfg.Save();
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败: " + ex.Message;
        }
    }

    private void Param_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        ReadParamsFromUi();
        ApplySolverSettings();
        ApplyRestitutionToExisting();
        foreach (var c in _coins)
            SyncVisual(c.Body, c.Shape);
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void WriteParamsToUi()
    {
        PadPxBox.Text = PiggyBankSettings.F(_cfg.PadPx);
        RestitutionBox.Text = PiggyBankSettings.F(_cfg.Restitution);
        StillSecBox.Text = PiggyBankSettings.F(_cfg.StillSeconds);
        JitterPosBox.Text = PiggyBankSettings.F(_cfg.JitterPosPx);
        JitterAngBox.Text = PiggyBankSettings.F(_cfg.JitterAng);
        BaumgarteBox.Text = PiggyBankSettings.F(_cfg.Baumgarte);
        LinearSlopBox.Text = PiggyBankSettings.F(_cfg.LinearSlop);
        MaxLinCorrBox.Text = PiggyBankSettings.F(_cfg.MaxLinearCorrection);
        PosIterBox.Text = PiggyBankSettings.F(_cfg.PositionIterations);
        VelIterBox.Text = PiggyBankSettings.F(_cfg.VelocityIterations);
    }

    private void ReadParamsFromUi()
    {
        _cfg.PadPx = ParseDouble(PadPxBox?.Text, _cfg.PadPx);
        _cfg.Restitution = (float)ParseDouble(RestitutionBox?.Text, _cfg.Restitution);
        _cfg.StillSeconds = (float)ParseDouble(StillSecBox?.Text, _cfg.StillSeconds);
        _cfg.JitterPosPx = (float)ParseDouble(JitterPosBox?.Text, _cfg.JitterPosPx);
        _cfg.JitterAng = (float)ParseDouble(JitterAngBox?.Text, _cfg.JitterAng);
        _cfg.Baumgarte = (float)ParseDouble(BaumgarteBox?.Text, _cfg.Baumgarte);
        _cfg.LinearSlop = (float)ParseDouble(LinearSlopBox?.Text, _cfg.LinearSlop);
        _cfg.MaxLinearCorrection = (float)ParseDouble(MaxLinCorrBox?.Text, _cfg.MaxLinearCorrection);
        _cfg.PositionIterations = (int)Math.Round(ParseDouble(PosIterBox?.Text, _cfg.PositionIterations));
        _cfg.VelocityIterations = (int)Math.Round(ParseDouble(VelIterBox?.Text, _cfg.VelocityIterations));
        _cfg.Clamp();
    }

    private void ApplySolverSettings()
    {
        // Aether 2.2：Baumgarte / LinearSlop / MaxLinearCorrection 为 const，无法运行时改。
        // 仍写入 piggybank-settings.json，迁主程序或换引擎时可直接用。
        Settings.PositionIterations = _cfg.PositionIterations;
        Settings.VelocityIterations = _cfg.VelocityIterations;
        _solver.PositionIterations = _cfg.PositionIterations;
        _solver.VelocityIterations = _cfg.VelocityIterations;
    }

    private void ApplyRestitutionToExisting()
    {
        foreach (var c in _coins)
        {
            foreach (var f in c.Body.FixtureList)
                f.Restitution = _cfg.Restitution;
        }
    }

    private static double ParseDouble(string? raw, double fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            return v;
        return fallback;
    }

    private void Stage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RebuildJar();
    }

    private void RebuildJar()
    {
        var w = (float)Math.Max(100, Stage.ActualWidth);
        var h = (float)Math.Max(100, Stage.ActualHeight);
        if (w < 10 || h < 10) return;

        var jarWpx = Math.Min(280, w * 0.42f);
        var jarHpx = Math.Min(220, h * 0.45f);
        var cx = w * 0.5f;
        var bottomPx = h - 36f;
        var topPx = bottomPx - jarHpx;
        var leftPx = cx - jarWpx * 0.5f;
        var rightPx = cx + jarWpx * 0.5f;

        _jarLeftX = leftPx / Ppm;
        _jarRightX = rightPx / Ppm;
        _jarBottomY = bottomPx / Ppm;
        _jarTopY = topPx / Ppm;
        _wallT = 10f / Ppm;

        if (_jarFloor is not null) _world.Remove(_jarFloor);
        if (_jarLeft is not null) _world.Remove(_jarLeft);
        if (_jarRight is not null) _world.Remove(_jarRight);

        var floorW = _jarRightX - _jarLeftX;
        var sideH = _jarBottomY - _jarTopY;

        _jarFloor = _world.CreateBody(new AetherVector2((_jarLeftX + _jarRightX) * 0.5f, _jarBottomY - _wallT * 0.5f));
        _jarFloor.BodyType = BodyType.Static;
        var floorFix = _jarFloor.CreateRectangle(floorW, _wallT, 1f, AetherVector2.Zero);
        floorFix.Friction = 0.55f;
        floorFix.Restitution = 0.02f;

        _jarLeft = _world.CreateBody(new AetherVector2(_jarLeftX + _wallT * 0.5f, (_jarTopY + _jarBottomY) * 0.5f));
        _jarLeft.BodyType = BodyType.Static;
        var leftFix = _jarLeft.CreateRectangle(_wallT, sideH, 1f, AetherVector2.Zero);
        leftFix.Friction = 0.4f;

        _jarRight = _world.CreateBody(new AetherVector2(_jarRightX - _wallT * 0.5f, (_jarTopY + _jarBottomY) * 0.5f));
        _jarRight.BodyType = BodyType.Static;
        var rightFix = _jarRight.CreateRectangle(_wallT, sideH, 1f, AetherVector2.Zero);
        rightFix.Friction = 0.4f;

        if (_jarDraw is null)
        {
            _jarDraw = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x6A, 0x7A, 0x8A)),
                StrokeThickness = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x90, 0xA0)),
                RadiusX = 4,
                RadiusY = 4,
                IsHitTestVisible = false
            };
            Stage.Children.Insert(0, _jarDraw);
        }

        Canvas.SetLeft(_jarDraw, leftPx);
        Canvas.SetTop(_jarDraw, topPx);
        _jarDraw.Width = jarWpx;
        _jarDraw.Height = jarHpx;
        _jarBuilt = true;
    }

    private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Stage);
        SpawnCoin((float)p.X / Ppm, (float)p.Y / Ppm, throwTowardJar: false);
    }

    private void ThrowIntoJar_Click(object sender, RoutedEventArgs e)
    {
        if (!_jarBuilt) return;
        var midX = (_jarLeftX + _jarRightX) * 0.5f;
        var spawnX = midX + (float)(_rng.NextDouble() * 0.5 - 0.25);
        var spawnY = Math.Max(0.2f, _jarTopY - 0.6f - (float)_rng.NextDouble() * 0.4f);
        SpawnCoin(spawnX, spawnY, throwTowardJar: true);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _coins)
        {
            _world.Remove(c.Body);
            Stage.Children.Remove(c.Shape);
        }
        _coins.Clear();
        UpdateStatus();
    }

    private void SpawnCoin(float xM, float yM, bool throwTowardJar)
    {
        if (!_jarBuilt) return;

        var body = _world.CreateBody(new AetherVector2(xM, yM));
        body.BodyType = BodyType.Dynamic;
        body.Rotation = (float)(_rng.NextDouble() * Math.PI * 2);
        body.SleepingAllowed = true;
        var fix = body.CreateRectangle(CoinW, CoinH, CoinDensity, AetherVector2.Zero);
        fix.Friction = CoinFriction;
        fix.Restitution = _cfg.Restitution;

        if (throwTowardJar)
        {
            var targetX = (_jarLeftX + _jarRightX) * 0.5f + (float)(_rng.NextDouble() * 0.3 - 0.15);
            var vx = (targetX - xM) * (2.5f + (float)_rng.NextDouble() * 2f);
            var vy = 1.5f + (float)_rng.NextDouble() * 2.5f;
            body.LinearVelocity = new AetherVector2(vx, vy);
            body.AngularVelocity = (float)(_rng.NextDouble() * 10 - 5);
        }
        else
        {
            body.LinearVelocity = new AetherVector2(
                (float)(_rng.NextDouble() * 4 - 2),
                (float)(_rng.NextDouble() * 3 + 0.5));
            body.AngularVelocity = (float)(_rng.NextDouble() * 14 - 7);
        }

        var shape = new Rectangle
        {
            Fill = new LinearGradientBrush(
                Color.FromRgb(0xF6, 0xD0, 0x55),
                Color.FromRgb(0xC4, 0x8A, 0x18),
                90),
            Stroke = new SolidColorBrush(Color.FromRgb(0x8A, 0x60, 0x10)),
            StrokeThickness = 1,
            RadiusX = 1,
            RadiusY = 1,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false
        };
        shape.RenderTransform = new RotateTransform();
        Stage.Children.Add(shape);
        _coins.Add(new CoinVisual(body, shape));
        SyncVisual(body, shape);
        UpdateStatus();
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var dt = (float)(now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (dt <= 0) return;
        if (dt > 0.05f) dt = 0.05f;

        _world.Step(dt, ref _solver);

        var maxY = (float)(Stage.ActualHeight / Ppm) + 2f;
        for (var i = _coins.Count - 1; i >= 0; i--)
        {
            var c = _coins[i];
            if (c.Body.Position.Y > maxY || c.Body.Position.Y < -3f
                || c.Body.Position.X < -3f || c.Body.Position.X > (float)(Stage.ActualWidth / Ppm) + 3f)
            {
                _world.Remove(c.Body);
                Stage.Children.Remove(c.Shape);
                _coins.RemoveAt(i);
                continue;
            }

            if (c.Body.BodyType == BodyType.Dynamic)
                c.PushSample(now, c.Body.Position.X, c.Body.Position.Y, c.Body.Rotation, _cfg.StillSeconds);
        }

        // 自下而上多轮：贴底/贴已固定 → 位置窗静止 → 固定（不改回 Dynamic）
        for (var pass = 0; pass < 8; pass++)
        {
            var froze = false;
            foreach (var c in _coins)
            {
                if (c.Body.BodyType != BodyType.Dynamic) continue;
                if (!CanAnchor(c.Body)) continue;
                if (!c.IsPositionStill(now, _cfg.StillSeconds, _cfg.JitterPosPx / Ppm, _cfg.JitterAng))
                    continue;
                Freeze(c);
                froze = true;
            }
            if (!froze) break;
        }

        var frozen = 0;
        foreach (var c in _coins)
        {
            if (c.Body.BodyType == BodyType.Static) frozen++;
            SyncVisual(c.Body, c.Shape);
        }
        UpdateStatus(frozen);
    }

    private bool CanAnchor(Body body)
    {
        for (var edge = body.ContactList; edge is not null; edge = edge.Next)
        {
            if (edge.Contact is null || !edge.Contact.IsTouching) continue;
            var other = edge.Other;
            if (other is null) continue;
            if (ReferenceEquals(other, _jarFloor))
                return true;
            // 已固定金币（排除罐壁）
            if (other.BodyType == BodyType.Static
                && !ReferenceEquals(other, _jarLeft)
                && !ReferenceEquals(other, _jarRight)
                && !ReferenceEquals(other, _jarFloor))
                return true;
        }
        return false;
    }

    private static void Freeze(CoinVisual c)
    {
        c.Body.LinearVelocity = AetherVector2.Zero;
        c.Body.AngularVelocity = 0;
        c.Body.BodyType = BodyType.Static;
        c.Shape.Stroke = new SolidColorBrush(Color.FromRgb(0x5A, 0x70, 0x40));
    }

    private void SyncVisual(Body body, Rectangle shape)
    {
        var pad = _cfg.PadPx;
        shape.Width = CoinW * Ppm + pad * 2;
        shape.Height = CoinH * Ppm + pad * 2;
        var px = body.Position.X * Ppm - shape.Width * 0.5;
        var py = body.Position.Y * Ppm - shape.Height * 0.5;
        Canvas.SetLeft(shape, px);
        Canvas.SetTop(shape, py);
        if (shape.RenderTransform is RotateTransform rot)
            rot.Angle = body.Rotation * 180.0 / Math.PI;
    }

    private void UpdateStatus(int frozen = -1)
    {
        if (frozen < 0)
            frozen = _coins.Count(c => c.Body.BodyType == BodyType.Static);
        StatusText.Text = $"金币 {_coins.Count} · 已固定 {frozen}";
    }

    private sealed class CoinVisual(Body body, Rectangle shape)
    {
        public Body Body { get; } = body;
        public Rectangle Shape { get; } = shape;
        private readonly List<(DateTime T, float X, float Y, float Rot)> _samples = new();

        public void PushSample(DateTime now, float x, float y, float rot, float windowSec)
        {
            _samples.Add((now, x, y, rot));
            var cut = now.AddSeconds(-Math.Max(windowSec, 0.05) - 0.05);
            while (_samples.Count > 0 && _samples[0].T < cut)
                _samples.RemoveAt(0);
        }

        public bool IsPositionStill(DateTime now, float windowSec, float jitterPosM, float jitterAng)
        {
            if (_samples.Count < 2) return false;
            var oldest = _samples[0].T;
            if ((now - oldest).TotalSeconds < windowSec * 0.95) return false;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minR = float.MaxValue, maxR = float.MinValue;
            var r0 = _samples[0].Rot;
            foreach (var s in _samples)
            {
                minX = Math.Min(minX, s.X);
                maxX = Math.Max(maxX, s.X);
                minY = Math.Min(minY, s.Y);
                maxY = Math.Max(maxY, s.Y);
                // 相对首样本展开，避免 ±π 跳变
                var dr = s.Rot - r0;
                while (dr > MathF.PI) dr -= MathF.Tau;
                while (dr < -MathF.PI) dr += MathF.Tau;
                minR = Math.Min(minR, dr);
                maxR = Math.Max(maxR, dr);
            }

            var dx = maxX - minX;
            var dy = maxY - minY;
            var span = MathF.Sqrt(dx * dx + dy * dy);
            return span <= jitterPosM && (maxR - minR) <= jitterAng;
        }
    }
}
