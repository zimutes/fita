using System.Globalization;
using Fita.Audio;
using Fita.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fita.Views;

public partial class MainWindow
{
    private readonly List<LevelSlider> _bandSliders = [];
    private readonly List<ValueBox> _bandValues = [];
    private readonly List<TextBlock> _bandLabels = [];
    private LevelSlider _preamp = null!;
    private ValueBox _preampValue = null!;

    private EqSettings Eq => _settings.Eq;

    private void SetupEq()
    {
        EqOn.IsChecked = Eq.Enabled;
        EqOn.IsCheckedChanged += (_, _) =>
        {
            Eq.Enabled = EqOn.IsChecked == true;
            ApplyEq();
        };
        EqAuto.IsChecked = Eq.AutoPreamp;
        EqAuto.IsCheckedChanged += (_, _) =>
        {
            Eq.AutoPreamp = EqAuto.IsChecked == true;
            ApplyEq();
        };
        PresetButton.Click += (_, _) => PresetMenu().ShowAt(PresetButton);
        SavePresetButton.Click += (_, _) => AskPresetName();
        ResetEqButton.Click += (_, _) =>
        {
            EqPresets.Apply(Eq, EqPresets.BuiltIn[0]);
            Eq.Preamp = 0;
            ApplyEq();
        };

        Graph.BandChanged += _ =>
        {
            Eq.Preset = "";
            ApplyEq(fromGraph: true);
        };
        Graph.DetailsRequested += (band, _) => ShowBandDetails(band, Graph);

        BuildBandSliders();
        ApplyEq();
    }

    /// <summary>Monta as colunas: pré-amplificador, separação e uma coluna por banda.</summary>
    private void BuildBandSliders()
    {
        BandGrid.Children.Clear();
        BandGrid.ColumnDefinitions.Clear();
        _bandSliders.Clear();
        _bandValues.Clear();
        _bandLabels.Clear();

        BandGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)));
        BandGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(10)));
        for (var i = 0; i < Eq.Bands.Count; i++)
            BandGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        (_preamp, _preampValue, var preLabel) = Column(0, Loc.T("Preamp"));
        ToolTip.SetTip(_preamp, Loc.T("PreampTip"));
        preLabel.Bind(TextBlock.TextProperty, this.GetResourceObservable("LU.Preamp"));
        _preamp.UserChanged += (_, v) =>
        {
            Eq.Preamp = Math.Round(v * 2) / 2;
            ApplyEq();
        };
        _preampValue.Committed += (_, v) =>
        {
            Eq.Preamp = v;
            ApplyEq();
        };

        var divider = new Border { Width = 1, Margin = new Thickness(4, 14, 4, 18), HorizontalAlignment = HorizontalAlignment.Center };
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("Divider"));
        Grid.SetColumn(divider, 1);
        BandGrid.Children.Add(divider);

        for (var i = 0; i < Eq.Bands.Count; i++)
        {
            var band = i;
            var (slider, value, label) = Column(i + 2, "");
            slider.UserChanged += (_, v) =>
            {
                Eq.Bands[band].Gain = Math.Round(v * 2) / 2;
                Eq.Preset = "";
                ApplyEq();
            };
            value.Committed += (_, v) =>
            {
                Eq.Bands[band].Gain = v;
                Eq.Preset = "";
                ApplyEq();
            };
            label.Cursor = new Cursor(StandardCursorType.Hand);
            label.PointerPressed += (_, e) =>
            {
                ShowBandDetails(band, label);
                e.Handled = true;
            };
            slider.ContextRequested += (_, e) =>
            {
                ShowBandDetails(band, slider);
                e.Handled = true;
            };
            _bandSliders.Add(slider);
            _bandValues.Add(value);
            _bandLabels.Add(label);
        }
    }

    private (LevelSlider Slider, ValueBox Value, TextBlock Label) Column(int column, string text)
    {
        var value = new ValueBox
        {
            Minimum = -EqSettings.MaxGain,
            Maximum = EqSettings.MaxGain,
            Step = 0.5,
            DefaultValue = 0,
            Formatter = Db,
            HorizontalAlignment = HorizontalAlignment.Center,
            Classes = { "band" },
        };
        value.Bind(ToolTip.TipProperty, this.GetResourceObservable("L.ValueTip"));
        var slider = new LevelSlider
        {
            Orientation = Orientation.Vertical,
            Minimum = -EqSettings.MaxGain,
            Maximum = EqSettings.MaxGain,
            Origin = 0,
            DefaultValue = 0,
            WheelStep = 0.5,
            Width = 26,
            Height = 76,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var label = new TextBlock { Text = text, Classes = { "label" }, FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, Children = { value, slider, label } };
        Grid.SetColumn(stack, column);
        BandGrid.Children.Add(stack);
        return (slider, value, label);
    }

    /// <summary>Envia o EQ para o motor e põe controlos, gráfico e textos a condizer.</summary>
    private void ApplyEq(bool fromGraph = false)
    {
        Eq.Sanitize();
        _engine.ConfigureEq(Eq);
        Graph.SetSettings(Eq, _engine.SampleRate);
        if (_bandSliders.Count != Eq.Bands.Count)
            BuildBandSliders();
        for (var i = 0; i < _bandSliders.Count; i++)
        {
            var b = Eq.Bands[i];
            if (!_bandSliders[i].IsDragging)
                _bandSliders[i].Value = b.UsesGain ? b.Gain : 0;
            _bandSliders[i].IsEnabled = b.UsesGain;
        }
        if (!_preamp.IsDragging)
            _preamp.Value = Eq.Preamp;
        if (EqOn.IsChecked != Eq.Enabled)
            EqOn.IsChecked = Eq.Enabled;
        if (EqAuto.IsChecked != Eq.AutoPreamp)
            EqAuto.IsChecked = Eq.AutoPreamp;
        RefreshEqTexts();
        Refresh();
    }

    private void RefreshEqTexts()
    {
        if (_bandSliders.Count == 0)
            return;
        for (var i = 0; i < _bandSliders.Count; i++)
        {
            var b = Eq.Bands[i];
            _bandValues[i].IsEnabled = b.UsesGain;
            _bandValues[i].Value = b.UsesGain ? b.Gain : 0;
            _bandLabels[i].Text = ShortHz(b.Frequency);
            ToolTip.SetTip(_bandLabels[i], $"{Loc.F("Band", i + 1)} · {EqGraph.FormatHz(b.Frequency)} · {Loc.T(b.Type.ToString())}");
        }
        _preampValue.Value = Eq.Preamp;
        var auto = EqMath.AutoPreampDb(Eq, _engine.SampleRate);
        var preset = Eq.Preset.Length == 0 ? Loc.T("Custom") : Loc.PresetName(Eq.Preset);
        PreampInfo.Text = Eq.AutoPreamp && auto < -0.05
            ? $"{preset} · {Loc.T("Auto")} {Db(auto)} dB"
            : preset;
    }

    private static string Db(double db) => db.ToString("+0.0;-0.0;0", CultureInfo.InvariantCulture);

    private static string ShortHz(double hz) =>
        hz >= 1000 ? (hz / 1000).ToString(hz % 1000 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + "k"
            : hz.ToString("0", CultureInfo.InvariantCulture);

    private MenuFlyout PresetMenu()
    {
        var menu = new MenuFlyout();
        foreach (var preset in EqPresets.BuiltIn)
        {
            var item = new MenuItem
            {
                Header = Loc.PresetName(preset.Name),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = Eq.Preset == preset.Name,
            };
            item.Click += (_, _) =>
            {
                EqPresets.Apply(Eq, preset);
                ApplyEq();
            };
            menu.Items.Add(item);
        }
        if (_settings.EqPresets.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var user in _settings.EqPresets.ToList())
            {
                var item = new MenuItem
                {
                    Header = user.Name,
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = Eq.Preset == user.Name,
                };
                item.Click += (_, _) =>
                {
                    Eq.Bands = user.Bands.Select(b => b.Clone()).ToList();
                    Eq.Preamp = user.Preamp;
                    Eq.Preset = user.Name;
                    ApplyEq();
                };
                menu.Items.Add(item);
            }
            var delete = new MenuItem { Header = Loc.T("DeletePreset").Split('“', '«')[0].Trim() };
            foreach (var user in _settings.EqPresets.ToList())
            {
                var d = new MenuItem { Header = Loc.F("DeletePreset", user.Name) };
                d.Click += (_, _) => _settings.EqPresets.Remove(user);
                delete.Items.Add(d);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(delete);
        }
        return menu;
    }

    private void AskPresetName()
    {
        var box = new TextBox { Classes = { "search" }, Text = Eq.Preset.Length > 0 && EqPresets.BuiltIn.All(p => p.Name != Eq.Preset) ? Eq.Preset : "" };
        box.Bind(TextBox.WatermarkProperty, this.GetResourceObservable("L.PresetName"));
        var save = new Button { Content = Loc.Upper ? Loc.T("Save").ToUpper(Loc.Culture) : Loc.T("Save"), Classes = { "small" } };
        var cancel = new Button { Content = Loc.Upper ? Loc.T("Cancel").ToUpper(Loc.Culture) : Loc.T("Cancel"), Classes = { "small" } };
        void Save()
        {
            var name = box.Text?.Trim();
            if (string.IsNullOrEmpty(name))
                return;
            _settings.EqPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            _settings.EqPresets.Add(new EqUserPreset { Name = name, Preamp = Eq.Preamp, Bands = Eq.Bands.Select(b => b.Clone()).ToList() });
            Eq.Preset = name;
            HideOverlay();
            ApplyEq();
            SettingsStore.Save(_settings);
        }
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => HideOverlay();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Save();
                e.Handled = true;
            }
        };
        ShowOverlay(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Heading(Loc.T("SavePreset")),
                box,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } },
            },
        });
        box.Focus();
    }

    /// <summary>Detalhes de uma banda: frequência, ganho, largura e tipo.</summary>
    private void ShowBandDetails(int index, Control anchor)
    {
        if (index < 0 || index >= Eq.Bands.Count)
            return;
        var band = Eq.Bands[index];

        NumericUpDown Number(double value, double min, double max, double step, string format) => new()
        {
            Value = (decimal)value, Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step,
            FormatString = format, Width = 120, Height = 28, MinHeight = 28, FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var freq = Number(band.Frequency, EqSettings.MinFrequency, EqSettings.MaxFrequency, band.Frequency >= 1000 ? 100 : 5, "0");
        var gain = Number(band.Gain, -EqSettings.MaxGain, EqSettings.MaxGain, 0.5, "+0.0;-0.0;0.0");
        var q = Number(band.Q, 0.2, 12, 0.05, "0.00");
        gain.IsEnabled = band.UsesGain;

        void Changed()
        {
            band.Frequency = (double)(freq.Value ?? 1000);
            band.Gain = (double)(gain.Value ?? 0);
            band.Q = (double)(q.Value ?? (decimal)EqSettings.DefaultQ);
            gain.IsEnabled = band.UsesGain;
            Eq.Preset = "";
            ApplyEq();
        }
        freq.ValueChanged += (_, _) => Changed();
        gain.ValueChanged += (_, _) => Changed();
        q.ValueChanged += (_, _) => Changed();

        var types = new WrapPanel { Orientation = Orientation.Horizontal };
        var toggles = new List<ToggleButton>();
        foreach (var type in Enum.GetValues<EqBandType>())
        {
            var t = new ToggleButton
            {
                Content = Loc.T(type.ToString()),
                IsChecked = band.Type == type,
                Classes = { "small" },
                Margin = new Thickness(0, 0, 4, 4),
            };
            t.Click += (_, _) =>
            {
                band.Type = type;
                foreach (var other in toggles)
                    other.IsChecked = other == t;
                if (type is EqBandType.LowShelf or EqBandType.HighShelf or EqBandType.LowCut or EqBandType.HighCut && Math.Abs(band.Q - EqSettings.DefaultQ) < 0.01)
                    q.Value = 0.71m;
                Changed();
            };
            toggles.Add(t);
            types.Children.Add(t);
        }

        var reset = new Button { Content = Loc.T("ResetBand"), Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            var iso = EqSettings.IsoFrequencies;
            band.Type = EqBandType.Peak;
            foreach (var other in toggles)
                other.IsChecked = Equals(other.Content, Loc.T(nameof(EqBandType.Peak)));
            freq.Value = (decimal)(index < iso.Length ? iso[index] : band.Frequency);
            gain.Value = 0;
            q.Value = (decimal)EqSettings.DefaultQ;
            Changed();
        };

        Grid Row(string label, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        var panel = new StackPanel
        {
            Width = 290,
            Children =
            {
                Heading($"{Loc.F("Band", index + 1)} · {EqGraph.FormatHz(band.Frequency)}"),
                new Border { Height = 8 },
                Row(Loc.T("Frequency") + " (Hz)", freq),
                Row(Loc.T("Gain") + " (dB)", gain),
                Row(Loc.T("Width"), q),
                new TextBlock { Text = Loc.T("Type"), Margin = new Thickness(0, 4, 0, 6) },
                types,
                new Border { Height = 4 },
                reset,
            },
        };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Top };
        flyout.ShowAt(anchor);
    }

    private TextBlock Heading(string text)
    {
        var heading = new TextBlock { Text = text, FontSize = 16, TextWrapping = TextWrapping.Wrap };
        heading.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("FontDisplay"));
        heading.Bind(TextBlock.FontWeightProperty, this.GetResourceObservable("TitleWeight"));
        return heading;
    }
}
