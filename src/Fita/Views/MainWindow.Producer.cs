using System.Globalization;
using System.Text;
using Fita.Audio;
using Fita.Controls;
using Fita.Library;
using Fita.Producer;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Fita.Views;

/// <summary>
/// O modo producer: a análise de cada faixa (na lista e no mostrador), o medidor ao vivo, o A/B
/// com as faixas de referência ao mesmo volume, as maneiras de ouvir (mono, lado, um canal, uma
/// banda sozinha), as notas no tempo e o relatório. Tudo dentro da janela que já existe: a lista
/// ganha uma coluna, o mostrador troca as linhas de baixo, o resto vai para o menu e as teclas.
/// </summary>
public partial class MainWindow
{
    /// <summary>No modo producer, com "igualar o volume", tudo toca a este volume.</summary>
    private const double MatchLufs = -18;

    private AnalysisQueue? _analysisQueue;
    private NotesStore? _notes;
    private DispatcherTimer? _compareTimer;
    private Track? _abMix;
    private Track? _abRef;
    private long _meterAt;
    private string _meterText = "";

    private bool Producer => _settings.ProducerMode;

    /// <summary>Depois de carregar a lista: as referências gravadas e, se estava ligado, o modo.</summary>
    private void SetupProducer()
    {
        MarkReferences(_playlist.Items);
        _playlist.Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is { Count: > 0 } added)
                MarkReferences(added.OfType<Track>());
            if (!Producer)
                return;
            RequestAnalysis();
            ScheduleCompare();
        };
        if (Producer)
            StartProducer();
    }

    private void MarkReferences(IEnumerable<Track> tracks)
    {
        if (_settings.ProducerReferences.Count == 0)
            return;
        var keys = _settings.ProducerReferences.ToHashSet(StringComparer.Ordinal);
        foreach (var t in tracks)
            t.IsReference = keys.Contains(t.Key);
    }

    private void SetProducer(bool on)
    {
        if (on == Producer)
            return;
        _settings.ProducerMode = on;
        if (on)
            StartProducer();
        else
            StopProducer();
        ShowStatus(Loc.T(on ? "ProducerOn" : "ProducerOff"));
        // O volume igualado muda o ganho da faixa que está a tocar: recomeça-a onde ia.
        if (_settings.ProducerMatch)
            ReplayAtPosition();
    }

    private void StartProducer()
    {
        if (_analysisQueue is null && (!_demo || _demoAnalysis))
        {
            _analysisQueue = new AnalysisQueue(() => _ffmpeg, AnalysisStore.Load());
            _analysisQueue.Analyzed += OnAnalyzed;
        }
        _notes ??= _demo ? null : NotesStore.Load();
        _engine.MeterOn = true;
        PlaylistBox.Classes.Set("producer", true);
        RequestAnalysis();
        ScheduleCompare();
        ShowTrackText();
        ShowProducerMarks();
        Refresh(force: true);
    }

    private void StopProducer()
    {
        _engine.MeterOn = false;
        _engine.Listen = MonitorMode.Stereo;
        _engine.Solo = -1;
        _analysisQueue?.Clear();
        PlaylistBox.Classes.Set("producer", false);
        ToolTip.SetTip(InfoText, null);
        ShowTrackText();
        ShowProducerMarks();
        Refresh(force: true);
    }

    private void RequestAnalysis()
    {
        if (_analysisQueue is null)
            return;
        var current = PlayingTrack ?? _playlist.Current;
        if (current is { Analysis: null })
            _analysisQueue.Request([current], urgent: true);
        _analysisQueue.Request(_playlist.Items.Where(t => t.Analysis is null).ToList());
    }

    private void OnAnalyzed(Track track, TrackAnalysis? analysis)
    {
        // Sem análise (ficheiro que não se lê): fica uma vazia, e a lista mostra um traço.
        track.Analysis = analysis ?? new TrackAnalysis();
        ScheduleCompare();
        if (track != _shownTrack)
            return;
        ShowTrackText();
        ShowProducerMarks();
        // A faixa que está a tocar ainda não tinha o volume medido: com o volume igualado, aplica-se já.
        if (Producer && _settings.ProducerMatch && analysis is { HasLoudness: true } && PlayingTrack == track)
            ReplayAtPosition();
    }

    private void ReplayAtPosition()
    {
        var snap = _engine.Snapshot();
        if (snap.State == PlayState.Playing && snap.Segment?.Item.Tag is Track playing)
            PlayTrack(playing, snap.Position);
    }

    /// <summary>O ganho do volume igualado (null fora do modo producer, ou sem medida).</summary>
    private float? MatchGain(Track track) =>
        Producer && _settings.ProducerMatch && track.Analysis is { HasLoudness: true } a
            ? (float)Math.Clamp(Math.Pow(10, (MatchLufs - a.Integrated) / 20), 0.05, 4)
            : null;

    // ── Comparação: referências ou média da lista ─────────────────────────────

    private void ScheduleCompare()
    {
        _compareTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _compareTimer!.Stop();
            Recompare();
        });
        _compareTimer.Stop();
        _compareTimer.Start();
    }

    private sealed record Comparison(double[]? Bands, double[]? Curve, bool AgainstReferences, double? Median);

    private static Comparison CompareWithin(IEnumerable<Track> scope)
    {
        var analyzed = scope.Where(t => t.Analysis is { HasLoudness: true }).ToList();
        var references = analyzed.Where(t => t.IsReference).Select(t => t.Analysis!).ToList();
        var against = references.Count > 0;
        var basis = against ? references : analyzed.Select(t => t.Analysis!).ToList();
        return new Comparison(Findings.Baseline(basis), Findings.BaselineCurve(basis), against,
            Findings.MedianLoudness(analyzed.Where(t => !t.IsReference).Select(t => t.Analysis!)));
    }

    private Comparison _comparison = new(null, null, false, null);

    private void Recompare()
    {
        _comparison = CompareWithin(_playlist.Items);
        foreach (var t in _playlist.Items)
            t.BandDeltas = t.Analysis is { HasLoudness: true } a ? Findings.Deltas(a, _comparison.Bands) : null;
        if (_shownTrack is not null)
            ShowTrackText();
    }

    private List<Finding> FindingsOf(Track track) =>
        track.Analysis is { } a ? Findings.For(a, track.BandDeltas, _comparison.AgainstReferences, track.IsReference ? null : _comparison.Median) : [];

    // ── Mostrador ─────────────────────────────────────────────────────────────

    /// <summary>A linha de baixo do título, no modo producer: os números da faixa.</summary>
    private string ProducerInfo(Track track)
    {
        var c = CultureInfo.InvariantCulture;
        if (track.Analysis is not { } a)
        {
            ToolTip.SetTip(InfoText, null);
            return _analysisQueue is { Pending: > 0 } q ? Loc.F("Analyzing", q.Pending) : track.Info;
        }
        if (!a.HasLoudness)
        {
            ToolTip.SetTip(InfoText, null);
            return Loc.T("NoAnalysis");
        }
        var findings = FindingsOf(track);
        ToolTip.SetTip(InfoText, findings.Count == 0 ? Loc.T("NoFindings") : string.Join("\n", findings.Select(f => f.Text)));
        var text = new StringBuilder();
        if (track.IsReference)
            text.Append("REF · ");
        text.Append(CultureInfo.InvariantCulture, $"{Minus(a.Integrated.ToString("0.0", c))} LUFS · TP {Signed(a.TruePeak)} · PLR {a.Plr.ToString("0.0", c)}");
        // Os avisos antes da gama: se a linha não couber, perde-se o menos importante.
        var serious = findings.Count(f => f.Severity != Severity.Info);
        if (serious > 0)
            text.Append(" · ").Append(Loc.F(serious == 1 ? "OneWarning" : "Warnings", serious));
        if (double.IsFinite(a.Range))
            text.Append(CultureInfo.InvariantCulture, $" · LRA {a.Range.ToString("0.0", c)}");
        return text.ToString();
    }

    /// <summary>A linha de estado a tocar, no modo producer: o medidor e como se está a ouvir.</summary>
    private string ProducerStatus()
    {
        var now = Environment.TickCount64;
        if (now - _meterAt < 100 && _meterText.Length > 0)
            return _meterText;
        _meterAt = now;
        var m = _engine.Meter;
        var c = CultureInfo.InvariantCulture;
        string L(double v) => double.IsFinite(v) ? Minus(v.ToString("0.0", c)) : "—";
        var parts = new List<string>
        {
            $"S {L(m.ShortTerm)}",
            $"M {L(m.Momentary)}",
            $"{Loc.T("Corr")} {(double.IsFinite(m.Correlation) ? Minus(m.Correlation.ToString("+0.00;-0.00", c)) : "—")}",
        };
        if (_engine.Listen != MonitorMode.Stereo)
            parts.Add(Loc.T("ListenShort." + _engine.Listen));
        if (_engine.Solo >= 0)
            parts.Add(Bands.Name(_engine.Solo).ToUpper(Loc.Culture));
        if (_settings.ProducerMatch && PlayingTrack?.Analysis is { HasLoudness: true })
            parts.Add($"={L(MatchLufs)}");
        if (PlayingTrack is { IsReference: true })
            parts.Add("REF");
        return _meterText = string.Join(" · ", parts);
    }

    private static string Minus(string text) => text.Replace('-', '−');

    private static string Signed(double db) =>
        double.IsFinite(db) ? Minus(db.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)) : "—";

    /// <summary>Saturações e notas na barra de posição.</summary>
    private void ShowProducerMarks()
    {
        var track = _shownTrack;
        if (!Producer || track is null || track.Duration <= 0)
        {
            Seek.ClipMarks = null;
            Seek.NoteMarks = null;
            return;
        }
        var d = track.Duration;
        Seek.ClipMarks = track.Analysis?.ClipTimes.Select(t => t / d).ToList();
        Seek.NoteMarks = _notes?.For(track).Select(n => n.Time / d).ToList();
    }

    // ── A/B com as referências ────────────────────────────────────────────────

    /// <summary>
    /// Tab: da faixa que se está a ouvir para a referência, no mesmo ponto, e de volta. Com o volume
    /// igualado ligado, as duas tocam ao mesmo volume.
    /// </summary>
    private void SwitchReference(bool nextReference = false)
    {
        var snap = _engine.Snapshot();
        var playing = snap.Segment?.Item.Tag as Track ?? _playlist.Current;
        var references = _playlist.Items.Where(t => t.IsReference).ToList();
        if (references.Count == 0)
        {
            ShowStatus(Loc.T("NoReference"));
            return;
        }
        Track? target;
        if (playing is { IsReference: true } && !nextReference)
            target = _abMix is not null && _playlist.IndexOf(_abMix) >= 0 ? _abMix : _playlist.Items.FirstOrDefault(t => !t.IsReference);
        else if (playing is { IsReference: true })
            target = references[(references.IndexOf(playing) + 1) % references.Count];
        else
        {
            _abMix = playing;
            target = _abRef is { IsReference: true } r && _playlist.IndexOf(r) >= 0 ? r : references[0];
        }
        if (target is null || target == playing)
            return;
        if (target.IsReference)
            _abRef = target;
        var at = snap.State == PlayState.Stopped ? 0 : snap.Position;
        if (target.Duration > 0)
            at = Math.Min(at, Math.Max(0, target.Duration - 1));
        PlayTrack(target, at);
        ShowStatus((target.IsReference ? "B · REF · " : "A · ") + target.Display);
    }

    private void ToggleReference(IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0)
            return;
        var on = !tracks.All(t => t.IsReference);
        foreach (var t in tracks)
        {
            t.IsReference = on;
            _settings.ProducerReferences.Remove(t.Key);
            if (on)
                _settings.ProducerReferences.Add(t.Key);
        }
        ScheduleCompare();
        ShowTrackText();
        ShowStatus(Loc.T(on ? "ReferenceOn" : "ReferenceOff"));
    }

    // ── Maneiras de ouvir ─────────────────────────────────────────────────────

    private void SetListen(MonitorMode mode)
    {
        _engine.Listen = mode;
        ShowStatus($"{Loc.T("Listen")}: {Loc.T("Listen." + mode)}");
    }

    /// <summary>O: estéreo → mono → lado → estéreo.</summary>
    private void CycleListen() => SetListen(_engine.Listen switch
    {
        MonitorMode.Stereo => MonitorMode.Mono,
        MonitorMode.Mono => MonitorMode.Side,
        _ => MonitorMode.Stereo,
    });

    /// <summary>1 a 6: uma banda sozinha (outra vez: todas); 0: todas.</summary>
    private void SetSolo(int band)
    {
        _engine.Solo = band == _engine.Solo ? -1 : band;
        var solo = _engine.Solo;
        ShowStatus(solo < 0 ? Loc.T("SoloOff") : Loc.F("SoloOn", Bands.LongName(solo), BandRange(solo)));
    }

    private static string BandRange(int band)
    {
        var (lo, hi) = Bands.Edges[band];
        return $"{EqGraph.FormatHz(lo)}–{EqGraph.FormatHz(hi)}";
    }

    /// <summary>As teclas do modo producer (fora dele não fazem nada).</summary>
    private bool ProducerKey(Key key, KeyModifiers modifiers)
    {
        if (!Producer)
            return false;
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl || alt)
        {
            if (ctrl && !alt && key == Key.R)
            {
                _ = GenerateReportAsync();
                return true;
            }
            return false;
        }
        switch (key)
        {
            case Key.Tab: SwitchReference(nextReference: shift); return true;
            case Key.O: CycleListen(); return true;
            case Key.N: AddNote(); return true;
            case >= Key.D1 and <= Key.D6: SetSolo(key - Key.D1); return true;
            case >= Key.NumPad1 and <= Key.NumPad6: SetSolo(key - Key.NumPad1); return true;
            case Key.D0 or Key.NumPad0:
                if (_engine.Solo >= 0)
                    SetSolo(_engine.Solo);
                return true;
            default: return false;
        }
    }

    // ── Menus ─────────────────────────────────────────────────────────────────

    private MenuItem ProducerMenu()
    {
        MenuItem Check(string header, bool on, Action click, string? gesture = null)
        {
            var item = new MenuItem
            {
                Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on,
                InputGesture = gesture is null ? null : KeyGesture.Parse(gesture),
            };
            item.Click += (_, _) => click();
            return item;
        }
        MenuItem Radio(string header, bool on, Action click, string? gesture = null)
        {
            var item = new MenuItem
            {
                Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = on,
                InputGesture = gesture is null ? null : KeyGesture.Parse(gesture),
            };
            item.Click += (_, _) => click();
            return item;
        }
        MenuItem Plain(string header, Action click, string? gesture = null)
        {
            var item = new MenuItem { Header = header, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
            item.Click += (_, _) => click();
            return item;
        }

        var menu = new MenuItem { Header = Loc.T("Producer") };
        menu.Items.Add(Check(Loc.T("ProducerMode"), Producer, () => SetProducer(!Producer), "Ctrl+P"));
        if (!Producer)
            return menu;

        menu.Items.Add(Check(Loc.F("MatchLoudness", Minus(MatchLufs.ToString("0", CultureInfo.InvariantCulture))), _settings.ProducerMatch, () =>
        {
            _settings.ProducerMatch = !_settings.ProducerMatch;
            ReplayAtPosition();
        }));
        var listen = new MenuItem { Header = Loc.T("Listen") };
        foreach (var mode in Enum.GetValues<MonitorMode>())
            listen.Items.Add(Radio(Loc.T("Listen." + mode), _engine.Listen == mode, () => SetListen(mode),
                mode is MonitorMode.Stereo or MonitorMode.Mono or MonitorMode.Side ? "O" : null));
        menu.Items.Add(listen);
        var solo = new MenuItem { Header = Loc.T("SoloBand") };
        solo.Items.Add(Radio(Loc.T("AllBands"), _engine.Solo < 0, () => SetSolo(_engine.Solo), "0"));
        for (var b = 0; b < Bands.Count; b++)
        {
            var band = b;
            solo.Items.Add(Radio($"{Bands.LongName(b)} ({BandRange(b)})", _engine.Solo == b, () =>
            {
                if (_engine.Solo != band)
                    SetSolo(band);
            }, (b + 1).ToString(CultureInfo.InvariantCulture)));
        }
        menu.Items.Add(solo);
        menu.Items.Add(new Separator());
        menu.Items.Add(Plain(Loc.T("ABSwitch"), () => SwitchReference(), "Tab"));
        if ((PlayingTrack ?? _playlist.Current) is { } current)
            menu.Items.Add(Check(Loc.T("IsReference"), current.IsReference, () => ToggleReference([current])));
        menu.Items.Add(Plain(Loc.T("AddNote"), AddNote, "N"));
        if ((PlayingTrack ?? _playlist.Current) is { } noted && _notes?.For(noted).Count > 0)
            menu.Items.Add(Plain(Loc.T("ShowNotes"), () => ShowNotes(noted)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Plain(Loc.T("Report"), () => _ = GenerateReportAsync(), "Ctrl+R"));
        return menu;
    }

    /// <summary>O que o modo producer junta ao menu do botão direito numa faixa.</summary>
    private IEnumerable<(string Text, Action? Action)> ProducerTrackItems(Track track)
    {
        if (!Producer)
            yield break;
        yield return ("-", null);
        var selected = SelectedTracks();
        if (!selected.Contains(track))
            selected = [track];
        yield return (Loc.T(selected.All(t => t.IsReference) ? "UnsetReference" : "SetReference"), () => ToggleReference(selected));
        if (_notes?.For(track).Count > 0)
            yield return (Loc.T("ShowNotes"), () => ShowNotes(track));
        if (track.Analysis is { HasLoudness: true })
            yield return (Loc.T("Findings"), () => ShowFindings(track));
    }

    // ── Notas ─────────────────────────────────────────────────────────────────

    private void AddNote()
    {
        var snap = _engine.Snapshot();
        var track = snap.Segment?.Item.Tag as Track ?? _playlist.Current;
        if (track is null || _notes is null)
            return;
        var time = snap.State == PlayState.Stopped ? 0 : snap.Position;
        var box = new TextBox { Classes = { "search" } };
        box.Bind(TextBox.WatermarkProperty, this.GetResourceObservable("L.NoteHint"));
        void Save()
        {
            var text = box.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return;
            _notes.Add(track, time, text);
            HideOverlay();
            ShowProducerMarks();
            ShowStatus(Loc.F("NoteSaved", Format.Time(time)));
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Save();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HideOverlay();
                e.Handled = true;
            }
        };
        ShowOverlay(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Heading(Loc.F("NoteAt", Format.Time(time))),
                new TextBlock { Text = track.Display, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { OverlayButton("Cancel", HideOverlay), OverlayButton("Save", Save) },
                },
            },
        });
        box.Focus();
    }

    private void ShowNotes(Track track)
    {
        if (_notes is null)
            return;
        var list = new StackPanel { Spacing = 4 };
        void Fill()
        {
            list.Children.Clear();
            foreach (var note in _notes.For(track).ToList())
            {
                var jump = new Button { Content = Format.Time(note.Time), Classes = { "small" }, MinWidth = 52 };
                jump.Click += (_, _) =>
                {
                    HideOverlay();
                    PlayTrack(track, note.Time);
                };
                var remove = new Button { Classes = { "small" } };
                remove.Bind(ContentControl.ContentProperty, this.GetResourceObservable("LU.Remove"));
                remove.Click += (_, _) =>
                {
                    _notes.Remove(track, note);
                    ShowProducerMarks();
                    Fill();
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
                row.Children.Add(jump);
                var text = new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                list.Children.Add(row);
            }
            if (list.Children.Count == 0)
                list.Children.Add(new TextBlock { Text = Loc.T("NoNotes"), Classes = { "muted" } });
        }
        Fill();
        ShowOverlay(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading(Loc.T("Notes")),
                new TextBlock { Text = track.Display, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis },
                new ScrollViewer { MaxHeight = 300, Content = list },
                OverlayButton("Close", HideOverlay),
            },
        });
    }

    private void ShowFindings(Track track)
    {
        var findings = FindingsOf(track);
        var list = new StackPanel { Spacing = 6 };
        foreach (var f in findings)
        {
            var line = new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap };
            if (f.Severity == Severity.Problem)
                line.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Brand"));
            else if (f.Severity == Severity.Info)
                line.Classes.Add("muted");
            list.Children.Add(line);
        }
        if (findings.Count == 0)
            list.Children.Add(new TextBlock { Text = Loc.T("NoFindings"), Classes = { "muted" } });
        ShowOverlay(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading(track.Display),
                new TextBlock { Text = ProducerInfo(track), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                new ScrollViewer { MaxHeight = 320, Content = list },
                OverlayButton("Close", HideOverlay),
            },
        });
    }

    // ── Relatório ─────────────────────────────────────────────────────────────

    private bool _reporting;

    /// <summary>
    /// Relatório das faixas escolhidas (ou da lista toda): um HTML que abre no navegador e um CSV
    /// ao lado. Espera pelas análises que faltam.
    /// </summary>
    private async Task GenerateReportAsync()
    {
        if (_analysisQueue is null || _reporting)
            return;
        var scope = SelectedTracks();
        if (scope.Count < 2)
            scope = [.. _playlist.Items];
        if (scope.Count == 0)
        {
            ShowStatus(Loc.T("ReportEmpty"));
            return;
        }
        _reporting = true;
        try
        {
            IStorageFolder? start = null;
            if (Directory.Exists(_settings.ReportFolder))
                start = await StorageProvider.TryGetFolderFromPathAsync(_settings.ReportFolder);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.T("ReportTitle"),
                SuggestedFileName = $"Fita {Loc.T("ReportFile")} {DateTime.Now:yyyy-MM-dd HHmm}.html",
                DefaultExtension = "html",
                FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html"] }],
                SuggestedStartLocation = start,
            });
            var path = file?.TryGetLocalPath();
            if (path is null)
                return;
            await WriteReportAsync(scope, path);
            _settings.ReportFolder = Path.GetDirectoryName(path) ?? "";
            ShowStatus(Loc.T("ReportDone"));
            await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception ex)
        {
            Log.Write("Falha no relatório: " + ex.Message);
            ShowStatus(ex.Message);
        }
        finally
        {
            _reporting = false;
        }
    }

    /// <summary>Escreve o relatório (HTML e CSV ao lado) destas faixas, à espera das análises que faltem.</summary>
    public async Task WriteReportAsync(IReadOnlyList<Track> scope, string path)
    {
        if (_analysisQueue is not null)
        {
            var missing = scope.Where(t => t.Analysis is null).ToList();
            if (missing.Count > 0)
            {
                ShowStatus(Loc.F("ReportWaiting", missing.Count));
                _analysisQueue.Request(missing, urgent: true);
                foreach (var t in missing)
                {
                    var a = await _analysisQueue.GetAsync(t);
                    t.Analysis ??= a ?? new TrackAnalysis();
                    if (missing.Count(m => m.Analysis is null) is > 0 and var left)
                        ShowStatus(Loc.F("ReportWaiting", left));
                }
            }
        }

        var comparison = CompareWithin(scope);
        var rows = scope.Where(t => t.Analysis is not null).Select(t =>
        {
            var a = t.Analysis!;
            var deltas = a.HasLoudness ? Findings.Deltas(a, comparison.Bands) : null;
            return new ReportTrack(t.Number, t.Display, t.Path, t.IsReference, a, deltas,
                Findings.For(a, deltas, comparison.AgainstReferences, t.IsReference ? null : comparison.Median),
                _notes?.For(t) ?? []);
        }).ToList();
        var info = new ReportInfo(Loc.T("ReportTitle"), DateTime.Now, comparison.Bands, comparison.Curve,
            comparison.AgainstReferences, comparison.Median);
        await File.WriteAllTextAsync(path, Report.Html(rows, info, ThemeManager.Current));
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".csv"), Report.Csv(rows), new UTF8Encoding(true));
    }

    private bool _demoAnalysis;

    /// <summary>Para as capturas: liga o modo producer de verdade (com análise) e diz quantas faltam.</summary>
    public int ProducerPending()
    {
        _demoAnalysis = true;
        if (!Producer)
            SetProducer(true);
        return _analysisQueue?.Pending ?? 0;
    }

    public IReadOnlyList<Track> PlaylistTracks => _playlist.Items;

    /// <summary>Para as verificações: marcar uma referência, tocar, e ver o que o motor tem.</summary>
    public void DemoReference(Track track) => ToggleReference([track]);

    public void DemoPlay(Track track, double at) => PlayTrack(track, at);

    public (EngineSnapshot Snapshot, MonitorMode Listen, int Solo) DemoEngine => (_engine.Snapshot(), _engine.Listen, _engine.Solo);

    // ── Capturas ──────────────────────────────────────────────────────────────

    /// <summary>Para as capturas: análises inventadas na lista de demonstração, sem ffmpeg.</summary>
    public void DemoProducer()
    {
        _settings.ProducerMode = true;
        var rnd = new Random(5);
        foreach (var t in _playlist.Items)
        {
            var i = -16 + rnd.NextDouble() * 8;
            t.Analysis = new TrackAnalysis
            {
                Integrated = Math.Round(i, 1), TruePeak = Math.Round(-3 + rnd.NextDouble() * 3.4, 1), Range = Math.Round(3 + rnd.NextDouble() * 6, 1),
                SamplePeak = -1, BandLevels = [.. Enumerable.Range(0, Bands.Count).Select(b => -8 - b * 1.5 + rnd.NextDouble() * 6)],
                Correlation = 0.8, Duration = t.Duration, ClipTimes = t.Number == 2 ? [42, 118.5] : [],
                Clips = t.Number == 2 ? 2 : 0,
            };
        }
        if (_playlist.Items.Count > 3)
            _playlist.Items[3].IsReference = true;
        StartProducer();
        Recompare();
    }
}
