using System.Globalization;
using System.Net;
using System.Text;
using Fita.Library;
using Fita.Themes;
using Avalonia.Media;

namespace Fita.Producer;

/// <summary>Uma faixa no relatório, já com a comparação e os avisos.</summary>
public sealed record ReportTrack(int Number, string Name, string Path, bool IsReference, TrackAnalysis Analysis,
    double[]? Deltas, IReadOnlyList<Finding> Findings, IReadOnlyList<Note> Notes);

/// <summary>O que vale para o relatório inteiro.</summary>
public sealed record ReportInfo(string Title, DateTime When, double[]? BaselineBands, double[]? BaselineCurve,
    bool AgainstReferences, double? Median);

/// <summary>
/// O relatório do modo producer: um HTML só (estilos e gráficos SVG lá dentro, abre em qualquer
/// navegador e imprime em PDF), com as cores do tema e na língua da interface; e um CSV com os
/// números, para uma folha de cálculo.
/// </summary>
public static class Report
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Html(IReadOnlyList<ReportTrack> tracks, ReportInfo info, ThemeDef theme)
    {
        var h = new StringBuilder();
        var lang = Loc.Language == UiLanguage.Pt ? "pt" : "en";
        h.Append($"<!doctype html><html lang=\"{lang}\"><head><meta charset=\"utf-8\">")
         .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
         .Append($"<title>{E(info.Title)} · {info.When.ToString("yyyy-MM-dd HH:mm", Inv)}</title>")
         .Append("<style>").Append(Css(theme)).Append("</style></head><body><main>");

        // Cabeçalho
        h.Append("<header>").Append(WordmarkSvg()).Append("<div>")
         .Append($"<h1>{E(info.Title)}</h1>")
         .Append($"<p class=\"muted\">{E(info.When.ToString(Loc.Language == UiLanguage.Pt ? "d 'de' MMMM 'de' yyyy, HH:mm" : "MMMM d, yyyy, HH:mm", Loc.Culture))}")
         .Append($" · {E(Loc.F(tracks.Count == 1 ? "R.OneTrack" : "R.Tracks", tracks.Count))}")
         .Append($" · {E(Loc.T(info.AgainstReferences ? "R.AgainstRefs" : "R.AgainstList"))}</p>")
         .Append("</div></header>");

        // Avisos de todas as faixas, primeiro: para ler em dez segundos.
        var serious = tracks.SelectMany(t => t.Findings.Where(f => f.Severity != Severity.Info).Select(f => (t, f))).ToList();
        h.Append($"<section><h2>{E(Loc.T("R.Findings"))}</h2>");
        if (serious.Count == 0)
            h.Append($"<p class=\"muted\">{E(Loc.T("R.NoFindings"))}</p>");
        else
        {
            h.Append("<ul class=\"findings\">");
            foreach (var (t, f) in serious.OrderBy(x => x.f.Severity).ThenBy(x => x.t.Number))
                h.Append($"<li class=\"{Css(f.Severity)}\"><a href=\"#t{t.Number}\">{t.Number}. {E(t.Name)}</a>: {E(f.Text)}</li>");
            h.Append("</ul>");
        }
        h.Append("</section>");

        // Resumo
        h.Append($"<section><h2>{E(Loc.T("R.Summary"))}</h2><div class=\"scroll\"><table class=\"summary\"><thead><tr>")
         .Append("<th>#</th>").Append($"<th class=\"name\">{E(Loc.T("R.Track"))}</th>")
         .Append("<th>LUFS</th><th>TP</th><th>PLR</th><th>LRA</th>")
         .Append($"<th>{E(Loc.T("Corr"))}</th>");
        for (var b = 0; b < Bands.Count; b++)
            h.Append($"<th title=\"{E(BandTitle(b))}\">{E(Bands.Name(b))}</th>");
        foreach (var p in Findings.Platforms)
            h.Append($"<th>{E(p.Name)}</th>");
        h.Append("</tr></thead><tbody>");
        foreach (var t in tracks)
        {
            var a = t.Analysis;
            h.Append($"<tr{(t.IsReference ? " class=\"ref\"" : "")}><td>{t.Number}</td><td class=\"name\"><a href=\"#t{t.Number}\">{E(t.Name)}</a>")
             .Append(t.IsReference ? " <span class=\"tag\">REF</span>" : "").Append("</td>");
            if (!a.HasLoudness)
            {
                h.Append($"<td colspan=\"{5 + Bands.Count + Findings.Platforms.Length}\" class=\"muted\">{E(Loc.T("NoAnalysis"))}</td></tr>");
                continue;
            }
            h.Append($"<td>{N(a.Integrated)}</td><td class=\"{(a.TruePeak > 0 ? "bad" : a.TruePeak > -1 ? "warn" : "")}\">{S(a.TruePeak)}</td>")
             .Append($"<td class=\"{(a.Plr < 7 ? "warn" : "")}\">{N(a.Plr)}</td><td>{N(a.Range)}</td>")
             .Append($"<td class=\"{(a.Correlation < 0 ? "bad" : a.Correlation < 0.2 ? "warn" : "")}\">{N(a.Correlation, "+0.00;-0.00")}</td>");
            for (var b = 0; b < Bands.Count; b++)
            {
                var d = t.Deltas is { } ds && b < ds.Length ? ds[b] : double.NaN;
                h.Append($"<td class=\"delta {(Math.Abs(d) >= 3 ? "big" : Math.Abs(d) >= 1.5 ? "mid" : "")}\">{(double.IsFinite(d) ? S(d) : "")}</td>");
            }
            foreach (var p in Findings.Platforms)
                h.Append($"<td>{S(p.Gain(a))}</td>");
            h.Append("</tr>");
        }
        h.Append("</tbody></table></div>");
        h.Append($"<p class=\"note\">{E(Loc.T("R.SummaryNote"))}</p>");
        if (tracks.Count(t => t.Analysis.HasLoudness) >= 2)
            h.Append(AlbumSvg(tracks, info.Median));
        h.Append("</section>");

        // Uma secção por faixa
        foreach (var t in tracks)
            Track(h, t, info);

        h.Append($"<footer class=\"muted\">Fita · by zimutek · {E(Loc.T("R.Footer"))}</footer>");
        h.Append("</main></body></html>");
        return h.ToString();
    }

    private static void Track(StringBuilder h, ReportTrack t, ReportInfo info)
    {
        var a = t.Analysis;
        h.Append($"<section class=\"track\" id=\"t{t.Number}\"><h2>{t.Number}. {E(t.Name)}")
         .Append(t.IsReference ? " <span class=\"tag\">REF</span>" : "").Append("</h2>")
         .Append($"<p class=\"path muted\">{E(t.Path)}</p>");
        if (!a.HasLoudness)
        {
            h.Append($"<p class=\"muted\">{E(Loc.T("NoAnalysis"))}</p></section>");
            return;
        }

        if (t.Findings.Count > 0)
        {
            h.Append("<ul class=\"findings\">");
            foreach (var f in t.Findings)
                h.Append($"<li class=\"{Css(f.Severity)}\">{E(f.Text)}</li>");
            h.Append("</ul>");
        }

        // Números
        var clips = a.Clips == 0 ? "0" : $"{a.Clips} ({string.Join(", ", a.ClipTimes.Take(8).Select(c => Format.Time(c)))}{(a.ClipTimes.Count > 8 ? "…" : "")})";
        (string, string)[] cells =
        [
            (Loc.T("R.Integrated"), $"{N(a.Integrated)} LUFS"),
            (Loc.T("R.Range"), $"{N(a.Range)} LU"),
            (Loc.T("R.TruePeak"), $"{S(a.TruePeak)} dBTP"),
            (Loc.T("R.SamplePeak"), $"{S(a.SamplePeak)} dBFS"),
            ("PLR", $"{N(a.Plr)} dB"),
            (Loc.T("R.ShortMax"), $"{N(a.ShortTermMax)} LUFS"),
            (Loc.T("R.MomentaryMax"), $"{N(a.MomentaryMax)} LUFS"),
            (Loc.T("Corr"), N(a.Correlation, "+0.00;-0.00")),
            (Loc.T("R.Clips"), clips),
            ("DC", $"{N(a.DcOffset)} dBFS"),
            (Loc.T("R.Silence"), $"{N(a.SilenceStart)} s · {N(a.SilenceEnd)} s"),
            (Loc.T("R.Format"), $"{(a.SampleRate / 1000.0).ToString("0.#", Inv)} kHz · {(a.Channels == 1 ? Loc.T("Mono") : Loc.T("Stereo"))} · {Format.Time(a.Duration)}"),
        ];
        h.Append("<dl class=\"metrics\">");
        foreach (var (k, v) in cells)
            h.Append($"<div><dt>{E(k)}</dt><dd>{E(v)}</dd></div>");
        h.Append("</dl>");

        // Linha do tempo
        h.Append($"<h3>{E(Loc.T("R.Timeline"))}</h3>").Append(TimelineSvg(a, t.Notes));

        // Balanço tonal
        h.Append($"<h3>{E(Loc.T("R.Tonal"))}</h3>").Append(TonalSvg(a, info.BaselineCurve, info.AgainstReferences));
        h.Append("<div class=\"scroll\"><table class=\"bands\"><thead><tr><th></th>");
        for (var b = 0; b < Bands.Count; b++)
            h.Append($"<th title=\"{E(BandTitle(b))}\">{E(Bands.Name(b))}</th>");
        h.Append("</tr></thead><tbody>");
        void Row(string label, Func<int, string> cell, Func<int, string>? cls = null)
        {
            h.Append($"<tr><th>{E(label)}</th>");
            for (var b = 0; b < Bands.Count; b++)
                h.Append($"<td class=\"{cls?.Invoke(b) ?? ""}\">{cell(b)}</td>");
            h.Append("</tr>");
        }
        Row(Loc.T("R.Level"), b => N(a.BandLevels.ElementAtOrDefault(b)));
        if (t.Deltas is { } deltas)
            Row(Loc.T(info.AgainstReferences ? "R.VsRefs" : "R.VsList"), b => S(deltas[b]),
                b => Math.Abs(deltas[b]) >= 3 ? "big" : Math.Abs(deltas[b]) >= 1.5 ? "mid" : "");
        Row(Loc.T("Corr"), b => N(a.BandCorrelation.ElementAtOrDefault(b), "+0.00;-0.00"),
            b => a.BandCorrelation.ElementAtOrDefault(b) < 0 ? "bad" : a.BandCorrelation.ElementAtOrDefault(b) < 0.5 ? "warn" : "");
        Row(Loc.T("R.MonoLoss"), b => N(a.MonoLoss.ElementAtOrDefault(b)), b => a.MonoLoss.ElementAtOrDefault(b) < -3 ? "warn" : "");
        Row(Loc.T("R.Width"), b => N(a.BandWidth.ElementAtOrDefault(b)));
        h.Append("</tbody></table></div>");

        // Streaming
        h.Append($"<h3>{E(Loc.T("R.Streaming"))}</h3><div class=\"scroll\"><table class=\"stream\"><thead><tr>");
        foreach (var p in Findings.Platforms)
            h.Append($"<th>{E(p.Name)}</th>");
        h.Append("</tr></thead><tbody><tr>");
        foreach (var p in Findings.Platforms)
        {
            var g = p.Gain(a);
            h.Append($"<td>{S(g)} dB<br><span class=\"muted\">{E(Loc.F("R.PlaysAt", N(a.Integrated + g)))}</span></td>");
        }
        h.Append("</tr></tbody></table></div>");

        if (t.Notes.Count > 0)
        {
            h.Append($"<h3>{E(Loc.T("Notes"))}</h3><ul class=\"notes\">");
            foreach (var n in t.Notes)
                h.Append($"<li><b>{Format.Time(n.Time)}</b> {E(n.Text)}</li>");
            h.Append("</ul>");
        }
        h.Append("</section>");
    }

    // ── Gráficos ──────────────────────────────────────────────────────────────

    private const double ChartW = 680, Left = 44, Right = 12;

    /// <summary>Volume de curta duração ao longo da faixa, o integrado, a correlação por baixo e as marcas.</summary>
    private static string TimelineSvg(TrackAnalysis a, IReadOnlyList<Note> notes)
    {
        const double top = 10, loudH = 120, corrTop = 146, corrH = 36, height = 196;
        var dur = Math.Max(1, a.Duration);
        double X(double t) => Left + t / dur * (ChartW - Left - Right);
        double Y(double lufs) => top + Math.Clamp((0 - lufs) / 40, 0, 1) * loudH;
        var s = new StringBuilder();
        s.Append($"<svg class=\"chart\" viewBox=\"0 0 {ChartW} {height}\" role=\"img\">");
        foreach (var level in new[] { 0, -10, -20, -30, -40 })
            s.Append($"<line class=\"grid\" x1=\"{Left}\" x2=\"{ChartW - Right}\" y1=\"{F(Y(level))}\" y2=\"{F(Y(level))}\"/>")
             .Append($"<text class=\"axis\" x=\"{Left - 6}\" y=\"{F(Y(level) + 3)}\" text-anchor=\"end\">{level.ToString(Inv).Replace('-', '−')}</text>");
        // Entre quatro e oito marcas, em passos redondos.
        var step = new[] { 5.0, 10, 15, 30, 60, 120, 300 }.FirstOrDefault(s => dur / s <= 8, 600);
        for (var t = 0.0; t <= dur; t += step)
            s.Append($"<text class=\"axis\" x=\"{F(X(t))}\" y=\"{height - 2}\" text-anchor=\"middle\">{Format.Time(t)}</text>");
        if (a.HasLoudness)
            s.Append($"<line class=\"integrated\" x1=\"{Left}\" x2=\"{ChartW - Right}\" y1=\"{F(Y(a.Integrated))}\" y2=\"{F(Y(a.Integrated))}\"><title>I {N(a.Integrated)} LUFS</title></line>");
        s.Append("<path class=\"line\" d=\"");
        var pen = false;
        for (var i = 0; i < a.ShortTerm.Length; i++)
        {
            var v = a.ShortTerm[i];
            if (!float.IsFinite(v))
            {
                pen = false;
                continue;
            }
            s.Append(pen ? 'L' : 'M').Append(F(X(i + 1))).Append(' ').Append(F(Y(v))).Append(' ');
            pen = true;
        }
        s.Append("\"/>");
        // Correlação: uma barra por segundo, para cima se positiva.
        var mid = corrTop + corrH / 2;
        s.Append($"<line class=\"grid\" x1=\"{Left}\" x2=\"{ChartW - Right}\" y1=\"{F(mid)}\" y2=\"{F(mid)}\"/>")
         .Append($"<text class=\"axis\" x=\"{Left - 6}\" y=\"{F(mid + 3)}\" text-anchor=\"end\">{E(Loc.T("Corr"))}</text>");
        var w = Math.Max(0.8, (ChartW - Left - Right) / Math.Max(1, a.CorrelationTimeline.Length) - 0.4);
        for (var i = 0; i < a.CorrelationTimeline.Length; i++)
        {
            var c = a.CorrelationTimeline[i];
            if (!float.IsFinite(c))
                continue;
            var hgt = Math.Abs(c) * corrH / 2;
            var y = c >= 0 ? mid - hgt : mid;
            s.Append($"<rect class=\"{(c < 0 ? "neg" : c < 0.3 ? "wide" : "corr")}\" x=\"{F(X(i))}\" y=\"{F(y)}\" width=\"{F(w)}\" height=\"{F(Math.Max(0.5, hgt))}\"/>");
        }
        foreach (var c in a.ClipTimes)
            s.Append($"<line class=\"clip\" x1=\"{F(X(c))}\" x2=\"{F(X(c))}\" y1=\"{top}\" y2=\"{top + loudH}\"><title>{E(Loc.T("R.Clip"))} {Format.Time(c)}</title></line>");
        foreach (var n in notes)
            s.Append($"<path class=\"notemark\" d=\"M{F(X(n.Time) - 5)} {top + loudH + 10} L{F(X(n.Time) + 5)} {top + loudH + 10} L{F(X(n.Time))} {top + loudH + 2} Z\"><title>{Format.Time(n.Time)} {E(n.Text)}</title></path>");
        s.Append("</svg>");
        return s.ToString();
    }

    /// <summary>A curva em terços de oitava da faixa, por cima da de comparação.</summary>
    private static string TonalSvg(TrackAnalysis a, double[]? baseline, bool againstReferences)
    {
        const double top = 22, plotH = 150, height = 196;
        var values = a.Curve.Concat(baseline ?? []).Where(double.IsFinite).ToList();
        if (values.Count == 0)
            return "";
        var max = Math.Ceiling(values.Max() / 5) * 5;
        // Pelo menos 20 dB de altura (uma curva plana não pode dar uma escala de zero).
        var min = Math.Min(max - 20, Math.Max(max - 45, Math.Floor(values.Min() / 5) * 5));
        double X(double f) => Left + (Math.Log10(f) - Math.Log10(20)) / (Math.Log10(20000) - Math.Log10(20)) * (ChartW - Left - Right);
        double Y(double db) => top + Math.Clamp((max - db) / (max - min), 0, 1) * plotH;
        var s = new StringBuilder();
        s.Append($"<svg class=\"chart\" viewBox=\"0 0 {ChartW} {height}\" role=\"img\">");
        for (var db = max; db >= min; db -= 10)
            s.Append($"<line class=\"grid\" x1=\"{Left}\" x2=\"{ChartW - Right}\" y1=\"{F(Y(db))}\" y2=\"{F(Y(db))}\"/>")
             .Append($"<text class=\"axis\" x=\"{Left - 6}\" y=\"{F(Y(db) + 3)}\" text-anchor=\"end\">{db.ToString("0", Inv).Replace('-', '−')}</text>");
        foreach (var f in new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000 })
            s.Append($"<text class=\"axis\" x=\"{F(X(f))}\" y=\"{height - 2}\" text-anchor=\"middle\">{(f >= 1000 ? (f / 1000).ToString("0", Inv) + "k" : f.ToString("0", Inv))}</text>");
        for (var b = 0; b < Bands.Count; b++)
        {
            var (lo, hi) = Bands.Edges[b];
            if (b > 0)
                s.Append($"<line class=\"band\" x1=\"{F(X(lo))}\" x2=\"{F(X(lo))}\" y1=\"{top - 4}\" y2=\"{top + plotH}\"/>");
            var centre = Math.Sqrt(lo * hi);
            s.Append($"<text class=\"bandname\" x=\"{F(X(centre))}\" y=\"{top - 8}\" text-anchor=\"middle\">{E(Bands.Name(b))}</text>");
        }
        string Path(double[] curve)
        {
            var p = new StringBuilder();
            var pen = false;
            for (var i = 0; i < curve.Length && i < Bands.Thirds.Length; i++)
            {
                if (!double.IsFinite(curve[i]))
                {
                    pen = false;
                    continue;
                }
                p.Append(pen ? 'L' : 'M').Append(F(X(Bands.Thirds[i]))).Append(' ').Append(F(Y(curve[i]))).Append(' ');
                pen = true;
            }
            return p.ToString();
        }
        if (baseline is not null)
            s.Append($"<path class=\"baseline\" d=\"{Path(baseline)}\"><title>{E(Loc.T(againstReferences ? "R.VsRefs" : "R.VsList"))}</title></path>");
        s.Append($"<path class=\"line\" d=\"{Path(a.Curve)}\"/>");
        s.Append("</svg>");
        if (baseline is not null)
            s.Append($"<p class=\"legend\"><span class=\"key line\"></span>{E(Loc.T("R.ThisTrack"))} <span class=\"key baseline\"></span>{E(Loc.T(againstReferences ? "R.VsRefs" : "R.VsList"))}</p>");
        return s.ToString();
    }

    /// <summary>O volume integrado de cada faixa, com a linha do meio da lista.</summary>
    private static string AlbumSvg(IReadOnlyList<ReportTrack> tracks, double? median)
    {
        const double top = 12, plotH = 130, height = 170;
        var list = tracks.Where(t => t.Analysis.HasLoudness).ToList();
        var floor = Math.Min(-30, Math.Floor(list.Min(t => t.Analysis.Integrated) / 6) * 6 - 6);
        var max = Math.Ceiling(Math.Max(-6, list.Max(t => t.Analysis.Integrated)) / 2) * 2;
        double Y(double lufs) => top + Math.Clamp((max - lufs) / (max - floor), 0, 1) * plotH;
        var slot = (ChartW - Left - Right) / list.Count;
        var s = new StringBuilder();
        s.Append($"<svg class=\"chart\" viewBox=\"0 0 {ChartW} {height}\" role=\"img\">");
        for (var level = max; level >= floor; level -= 6)
            s.Append($"<line class=\"grid\" x1=\"{Left}\" x2=\"{ChartW - Right}\" y1=\"{F(Y(level))}\" y2=\"{F(Y(level))}\"/>")
             .Append($"<text class=\"axis\" x=\"{Left - 6}\" y=\"{F(Y(level) + 3)}\" text-anchor=\"end\">{level.ToString("0", Inv).Replace('-', '−')}</text>");
        for (var i = 0; i < list.Count; i++)
        {
            var t = list[i];
            var x = Left + i * slot + slot * 0.18;
            var y = Y(t.Analysis.Integrated);
            s.Append($"<rect class=\"{(t.IsReference ? "refbar" : "bar")}\" x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(slot * 0.64)}\" height=\"{F(top + plotH - y)}\">")
             .Append($"<title>{t.Number}. {E(t.Name)}: {N(t.Analysis.Integrated)} LUFS</title></rect>");
            if (list.Count <= 40)
                s.Append($"<text class=\"axis\" x=\"{F(x + slot * 0.32)}\" y=\"{height - 14}\" text-anchor=\"middle\">{t.Number}</text>");
        }
        if (median is { } m)
            s.Append($"<line class=\"integrated\" x1=\"{Left}\" x2=\"{ChartW - Right}\" y1=\"{F(Y(m))}\" y2=\"{F(Y(m))}\"><title>{E(Loc.T("R.Median"))} {N(m)} LUFS</title></line>");
        s.Append($"<text class=\"axis\" x=\"{ChartW - Right}\" y=\"{height - 1}\" text-anchor=\"end\">LUFS · {E(Loc.T("R.Median"))} {(median is { } md ? N(md) : "—")}</text>");
        s.Append("</svg>");
        return s.ToString();
    }

    /// <summary>O nome à maneira do logótipo (o mesmo desenho da barra de título).</summary>
    private static string WordmarkSvg()
    {
        var s = new StringBuilder("<svg class=\"wordmark\" viewBox=\"0 0 937 315\" aria-label=\"FITA\"><path fill-rule=\"evenodd\" d=\"");
        foreach (var outline in Controls.Wordmark.Shapes)
        {
            s.Append('M');
            for (var i = 0; i < outline.Length; i++)
                s.Append(i == 0 ? "" : "L").Append(F(outline[i].X)).Append(' ').Append(F(outline[i].Y)).Append(' ');
            s.Append('Z');
        }
        return s.Append("\"/></svg>").ToString();
    }

    // ── Estilo ────────────────────────────────────────────────────────────────

    private static string Css(ThemeDef t)
    {
        string C(Color c) => $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.###", Inv)})";
        var font = t.FontSans.Name;
        var mono = t.FontMono.Name;
        return $$"""
            :root{--bg:{{C(t.Base)}};--panel:{{C(t.PanelBg)}};--border:{{C(t.PanelBorder)}};--text:{{C(t.Text)}};--dim:{{C(t.TextDim)}};
            --muted:{{C(t.TextMuted)}};--accent:{{C(t.Accent)}};--alert:{{C(t.Brand)}};--line:{{C(t.Divider)}};--fill:{{C(t.SliderFill)}};
            --track:{{C(t.SliderTrack)}} }
            *{box-sizing:border-box}
            html{background:var(--bg);color:var(--text)}
            body{margin:0;font:14px/1.5 "{{font}}",system-ui,-apple-system,"Segoe UI",sans-serif}
            main{max-width:960px;margin:0 auto;padding:28px 20px 60px}
            header{display:flex;gap:18px;align-items:center;margin-bottom:18px}
            .wordmark{width:110px;height:auto;fill:var(--accent);flex:none}
            h1{margin:0;font-size:24px;line-height:1.2}
            h2{font-size:18px;margin:0 0 10px}
            h3{font-size:13px;letter-spacing:.06em;text-transform:uppercase;color:var(--dim);margin:20px 0 6px}
            section{background:var(--panel);border:1px solid var(--border);border-radius:{{t.PanelRadius.ToString("0", Inv)}}px;padding:18px 20px;margin:14px 0}
            a{color:var(--accent);text-decoration:none}a:hover{text-decoration:underline}
            .muted{color:var(--muted)}.path{font-size:12px;margin:-6px 0 10px;word-break:break-all}
            .tag{font:600 10px/1 "{{mono}}",ui-monospace,monospace;border:1px solid var(--accent);color:var(--accent);padding:2px 5px;border-radius:4px;vertical-align:middle}
            .findings{margin:0;padding-left:18px}.findings li{margin:3px 0}
            .findings .problem{color:var(--alert);font-weight:600}.findings .info{color:var(--dim)}
            .scroll{overflow-x:auto}
            table{border-collapse:collapse;width:100%;font:12px/1.4 "{{mono}}",ui-monospace,monospace}
            th,td{padding:5px 6px;text-align:right;border-bottom:1px solid var(--line);white-space:nowrap}
            th{color:var(--muted);font-weight:600}
            .name{text-align:left;max-width:220px;overflow:hidden;text-overflow:ellipsis;font-family:"{{font}}",system-ui,sans-serif}
            tr.ref td{color:var(--accent)}
            td.warn{color:var(--alert);opacity:.8}td.bad{color:var(--alert);font-weight:700}
            td.delta.mid{color:var(--accent)}td.delta.big{color:var(--alert);font-weight:700}
            td.mid{color:var(--accent)}td.big{color:var(--alert);font-weight:700}
            .bands th:first-child{text-align:left}
            .note,.legend{font-size:12px;color:var(--muted);margin:8px 0 0}
            dl.metrics{display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:8px 16px;margin:12px 0 0}
            dl.metrics div{border-left:2px solid var(--line);padding-left:9px}
            dt{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em}
            dd{margin:0;font:600 14px/1.4 "{{mono}}",ui-monospace,monospace}
            .chart{width:100%;height:auto;display:block;margin-top:4px}
            .chart .grid{stroke:var(--line);stroke-width:1}
            .chart .axis,.chart .bandname{fill:var(--muted);font:10px "{{mono}}",ui-monospace,monospace}
            .chart .bandname{fill:var(--dim)}
            .chart .band{stroke:var(--line);stroke-dasharray:2 3}
            .chart .line{fill:none;stroke:var(--accent);stroke-width:2;stroke-linejoin:round}
            .chart .baseline{fill:none;stroke:var(--dim);stroke-width:1.6;stroke-dasharray:5 4}
            .chart .integrated{stroke:var(--dim);stroke-width:1;stroke-dasharray:4 3}
            .chart .corr{fill:var(--fill);opacity:.55}.chart .wide{fill:var(--muted)}.chart .neg{fill:var(--alert)}
            .chart .clip{stroke:var(--alert);stroke-width:1.5;opacity:.85}
            .chart .notemark{fill:var(--accent)}
            .chart .bar{fill:var(--fill);opacity:.75}.chart .refbar{fill:var(--accent)}
            .legend .key{display:inline-block;width:18px;height:0;border-top:2px solid var(--accent);vertical-align:middle;margin:0 5px 0 0}
            .legend .key.baseline{border-top:2px dashed var(--dim);margin-left:14px}
            .notes{padding-left:18px;margin:0}.notes b{font-family:"{{mono}}",ui-monospace,monospace;margin-right:6px}
            footer{text-align:center;font-size:12px;margin-top:24px}
            @media print{html{background:#fff;color:#000}section{break-inside:avoid;border-color:#ccc;background:#fff}
            :root{--text:#000;--dim:#333;--muted:#666;--line:#ddd;--panel:#fff;--bg:#fff} }
            """;
    }

    private static string Css(Severity s) => s switch { Severity.Problem => "problem", Severity.Warning => "warning", _ => "info" };

    private static string BandTitle(int b)
    {
        var (lo, hi) = Bands.Edges[b];
        return $"{Bands.LongName(b)} · {Controls.EqGraph.FormatHz(lo)}–{Controls.EqGraph.FormatHz(hi)}";
    }

    // ── CSV ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uma linha por faixa. Em português com ";" e vírgula decimal (o que o Excel espera nesse
    /// idioma); em inglês com "," e ponto.
    /// </summary>
    public static string Csv(IReadOnlyList<ReportTrack> tracks)
    {
        var pt = Loc.Language == UiLanguage.Pt;
        var sep = pt ? ";" : ",";
        var culture = pt ? Loc.Culture : Inv;
        string V(double v) => double.IsFinite(v) ? v.ToString("0.##", culture) : "";
        string Q(string s) => s.Contains(sep) || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        var head = new List<string>
        {
            "#", Loc.T("R.Track"), Loc.T("R.File"), "REF", "LUFS", "LRA", "TP (dBTP)", Loc.T("R.SamplePeak") + " (dBFS)", "PLR",
            Loc.T("R.ShortMax"), Loc.T("R.MomentaryMax"), Loc.T("Corr"), Loc.T("R.Clips"), "DC (dBFS)",
            Loc.T("R.SilenceStart") + " (s)", Loc.T("R.SilenceEnd") + " (s)", Loc.T("R.Duration") + " (s)", "Hz",
        };
        for (var b = 0; b < Bands.Count; b++)
            head.Add($"{Bands.Name(b)} (dB)");
        for (var b = 0; b < Bands.Count; b++)
            head.Add($"{Bands.Name(b)} Δ");
        for (var b = 0; b < Bands.Count; b++)
            head.Add($"{Bands.Name(b)} {Loc.T("Corr")}");
        for (var b = 0; b < Bands.Count; b++)
            head.Add($"{Bands.Name(b)} {Loc.T("R.MonoLoss")}");
        foreach (var p in Findings.Platforms)
            head.Add($"{p.Name} (dB)");
        head.Add(Loc.T("R.Findings"));
        head.Add(Loc.T("Notes"));

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, head.Select(Q)));
        foreach (var t in tracks)
        {
            var a = t.Analysis;
            var row = new List<string>
            {
                t.Number.ToString(Inv), t.Name, t.Path, t.IsReference ? "1" : "", V(a.Integrated), V(a.Range), V(a.TruePeak), V(a.SamplePeak),
                V(a.Plr), V(a.ShortTermMax), V(a.MomentaryMax), V(a.Correlation), a.Clips.ToString(Inv), V(a.DcOffset),
                V(a.SilenceStart), V(a.SilenceEnd), V(a.Duration), a.SampleRate.ToString(Inv),
            };
            for (var b = 0; b < Bands.Count; b++)
                row.Add(V(a.BandLevels.ElementAtOrDefault(b, double.NaN)));
            for (var b = 0; b < Bands.Count; b++)
                row.Add(t.Deltas is { } d ? V(d[b]) : "");
            for (var b = 0; b < Bands.Count; b++)
                row.Add(V(a.BandCorrelation.ElementAtOrDefault(b, double.NaN)));
            for (var b = 0; b < Bands.Count; b++)
                row.Add(V(a.MonoLoss.ElementAtOrDefault(b, double.NaN)));
            foreach (var p in Findings.Platforms)
                row.Add(a.HasLoudness ? V(p.Gain(a)) : "");
            row.Add(string.Join(" | ", t.Findings.Select(f => f.Text)));
            row.Add(string.Join(" | ", t.Notes.Select(n => $"{Format.Time(n.Time)} {n.Text}")));
            sb.AppendLine(string.Join(sep, row.Select(Q)));
        }
        return sb.ToString();
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string N(double v, string format = "0.0") =>
        double.IsFinite(v) ? v.ToString(format, Inv).Replace('-', '−') : "—";

    private static string S(double v) => N(v, "+0.0;-0.0");

    private static string F(double v) => v.ToString("0.#", Inv);
}

internal static class ArrayExtensions
{
    public static double ElementAtOrDefault(this double[] array, int index, double fallback) =>
        index >= 0 && index < array.Length ? array[index] : fallback;
}
