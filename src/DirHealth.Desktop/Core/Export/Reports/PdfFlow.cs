using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UI = DirHealth.Desktop.Core.Export.PdfPageBuilder;

namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>Per-column look of a <see cref="PdfFlow.Table"/>.</summary>
internal enum CellKind { Text, Muted, Mono, Severity, Number, CheckStatus }

/// <summary>
/// Page flow shared by every DirHealth PDF: running header (brand · report name · domain/classification),
/// footer with "Page x of y" (second pass, when the total is known), page breaks that repeat the section
/// name ("… (continued)") and the column headers of a table, wrapped paragraphs, tables whose cells wrap
/// instead of being cut (F-02) and PowerShell blocks wrapped by <see cref="TextWrap.Code"/> (F-01).
/// The cursor <see cref="Y"/> is always the top of the next element. Every drawn text also goes to the
/// optional sink, exactly as drawn, so tests can read the PDF's content without parsing PDF streams.
/// </summary>
internal sealed class PdfFlow
{
    public const double Margin   = UI.Margin;
    public const double HeaderH  = 44;           // accent bar + brand line + hairline
    public const double FooterH  = 34;
    private const double CellLineH = 11;
    private const int    MaxCellLines = 30;      // a pathological cell must not need more than a page

    public readonly PdfDocument Doc = new();
    public readonly UI Ui = new();
    public PdfPage   Page = null!;
    public XGraphics G    = null!;
    public double    Y;
    /// <summary>Repeated as "… (continued)" at the top of a page that a break opened inside this section.</summary>
    public string    Continued = "";

    private readonly string _reportName;
    private readonly string _headerRight;
    private readonly Action<string>? _sink;

    public static readonly XSolidBrush Ink    = new(UI.ColBodyText);
    public static readonly XSolidBrush Ink2   = new(UI.ColText2);
    public static readonly XSolidBrush Muted  = new(UI.ColLabelGray);
    public static readonly XSolidBrush Accent = new(UI.ColAccent);

    public PdfFlow(string reportName, string headerRight, Action<string>? sink)
    {
        _reportName = reportName; _headerRight = headerRight; _sink = sink;
    }

    public double PageW  => Page.Width.Point;
    public double PageH  => Page.Height.Point;
    public double Width  => PageW - 2 * Margin;
    public double Bottom => PageH - FooterH - 14;
    public double Top    => HeaderH + 22;
    public bool   IsLandscape => Page.Orientation == PageOrientation.Landscape;
    public bool   AtTop  => Y <= Top + 20;

    // ── primitives ────────────────────────────────────────────────────────────
    public void Text(string text, XFont font, XBrush brush, double x, double baseline)
    {
        _sink?.Invoke(text);
        if (text.Length > 0) G.DrawString(text, font, brush, x, baseline);
    }

    /// <summary>Sends a text to the sink without drawing it — only for text drawn by a primitive outside this class.</summary>
    public void Sink(string text) => _sink?.Invoke(text);
    public Action<string>? SinkAction => _sink;

    public double W(string s, XFont font) => G.MeasureString(s, font).Width;

    public string Fit(string text, XFont font, double width)
    {
        if (string.IsNullOrEmpty(text) || W(text, font) <= width) return text ?? "";
        var s = text;
        while (s.Length > 1 && W(s + "…", font) > width) s = s[..^1];
        return s + "…";
    }

    public IReadOnlyList<string> Wrap(string? text, XFont font, double width) => TextWrap.Words(text, width, s => W(s, font));

    /// <summary>Baseline inside a line box of height <paramref name="lineH"/> starting at <paramref name="top"/>.</summary>
    public static double Base(double top, double lineH, XFont font) => top + (lineH + font.Size * 0.72) / 2;

    // ── pages ─────────────────────────────────────────────────────────────────
    public void NewPage(bool landscape = false, bool bare = false)
    {
        G?.Dispose();
        Page = Doc.AddPage();
        Page.Size = PageSize.A4;
        if (landscape) Page.Orientation = PageOrientation.Landscape;
        G = XGraphics.FromPdfPage(Page);
        if (bare) { Y = 0; return; }   // cover: no running header
        DrawHeader();
        Y = Top;
        if (Continued.Length > 0)
        {
            const string cont = " (continued)";
            Text(Fit(Continued, UI.F8B, Width - W(cont, UI.F8B)) + cont, UI.F8B, Muted, Margin, Y + 8);
            Y += 22;
        }
    }

    /// <summary>Breaks the page when <paramref name="h"/> does not fit. True when a new page was started.</summary>
    public bool Ensure(double h)
    {
        if (Y + h <= Bottom) return false;
        NewPage(IsLandscape);
        return true;
    }

    private void DrawHeader()
    {
        G.DrawRectangle(new XSolidBrush(UI.ColNavy), 0, 0, PageW, 4);
        Ui.DrawLogo(G, Margin, 15, 15);
        double x = Margin + 21;
        G.DrawString("DirHealth", UI.F9B, Ink, x, 26.5);
        x += W("DirHealth", UI.F9B) + 8;
        G.DrawLine(new XPen(UI.ColBorderDark, 0.8), x, 17, x, 29);
        x += 8;
        G.DrawString(_reportName, UI.F9, Ink2, x, 26.5);
        var right = Fit(_headerRight, UI.F7B, Width / 2);
        G.DrawString(right, UI.F7B, Muted, PageW - Margin - W(right, UI.F7B), 26);
        G.DrawLine(new XPen(UI.ColBorder, 0.8), Margin, HeaderH - 4, PageW - Margin, HeaderH - 4);
    }

    /// <summary>
    /// Title band of a one-subject report (password report, user dossier): navy band with overline, title,
    /// subtitle and optional markers — the same look as the cover of the dashboard report, on one band.
    /// </summary>
    public void Hero(string overline, string title, string subtitle, IReadOnlyList<(string Text, XColor Fill)>? pills = null)
    {
        var titleLines = Wrap(title, UI.F22B, Width - 160).Take(2).ToList();
        double h = 50 + titleLines.Count * 26 + 14;
        G.DrawRectangle(new XSolidBrush(UI.ColNavy), Margin, Y, Width, h);
        G.DrawRectangle(new XSolidBrush(UI.ColAccent), Margin, Y + h - 3, Width, 3);
        Text(overline, UI.F7B, new XSolidBrush(UI.ColOnDarkMuted), Margin + 20, Y + 26);
        double x = Margin + Width - 20;
        foreach (var (text, fill) in pills ?? [])
        {
            x -= W(text, UI.F7B) + 12;
            UI.Pill(G, text, UI.F7B, fill, XColors.White, x, Y + 26, sink: _sink);
            x -= 8;
        }
        double y = Y + 56;
        foreach (var line in titleLines) { Text(line, UI.F22B, new XSolidBrush(XColors.White), Margin + 20, y); y += 26; }
        Text(Fit(subtitle, UI.F9, Width - 40), UI.F9, new XSolidBrush(UI.ColOnDark), Margin + 20, y - 4);
        Y += h + 16;
    }

    /// <summary>A row of KPI tiles; a tile with value "0" is drawn neutral, so only real counts get colour.</summary>
    public void Tiles(IReadOnlyList<(string Value, string Label, XColor Accent)> tiles, double h = 62)
    {
        if (tiles.Count == 0) return;
        const double gap = 10;
        double w = (Width - gap * (tiles.Count - 1)) / tiles.Count;
        Ensure(h + 8);
        for (int i = 0; i < tiles.Count; i++)
        {
            var (value, label, accent) = tiles[i];
            bool zero = value == "0";
            UI.Tile(G, Margin + i * (w + gap), Y, w, h, value, label,
                    zero ? UI.ColBorderDark : accent, zero ? UI.ColSurface : UI.Tint(accent, 0.92),
                    zero ? UI.ColLabelGray : UI.Shade(accent, 0.6), _sink);
        }
        Y += h + 16;
    }

    // ── text blocks ───────────────────────────────────────────────────────────
    /// <summary>Section heading with an accent bar, kept on one page with at least <paramref name="keepWith"/> of content.</summary>
    public void Heading(string title, double keepWith = 70, XFont? font = null)
    {
        font ??= UI.F15B;
        Continued = "";
        double h = font.Size + 16;
        if (Y + h + keepWith > Bottom) NewPage(IsLandscape);
        else if (!AtTop) Y += 16;
        G.DrawRectangle(Accent, Margin, Y + 1, 3, font.Size + 2);
        Text(title, font, Ink, Margin + 11, Y + font.Size * 0.92 + 1);
        Y += h;
        Continued = title;
    }

    /// <summary>Small uppercase label with a hairline — sub-sections inside a section.</summary>
    public void Overline(string label, double keepWith = 30)
    {
        if (Y + 20 + keepWith > Bottom) NewPage(IsLandscape);
        Text(label, UI.F7B, Muted, Margin, Y + 8);
        G.DrawLine(new XPen(UI.ColBorder, 0.8), Margin, Y + 12, Margin + Width, Y + 12);
        Y += 20;
    }

    /// <summary>Prose, wrapped at blanks; words wider than the line (URLs, DNs) are broken by width.</summary>
    public void Paragraph(string text, XFont font, XBrush brush, double indent = 0, double lineH = 13, double? width = null)
    {
        double w = width ?? Width - indent;
        foreach (var line in Wrap(text, font, w))
        {
            Ensure(lineH);
            Text(line, font, brush, Margin + indent, Base(Y, lineH, font));
            Y += lineH;
        }
    }

    /// <summary>Label in a left column, wrapped text on the right — the "definition list" of the finding cards.</summary>
    public void Definition(string label, string text, XBrush? brush = null, double labelW = 96, XFont? font = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        font ??= UI.F9;
        const double lineH = 13;
        var lines = Wrap(text, font, Width - labelW);
        Ensure(Math.Min(lines.Count, 2) * lineH);
        Text(label, UI.F7B, Muted, Margin, Base(Y, lineH, font));
        foreach (var line in lines)
        {
            Ensure(lineH);
            Text(line, font, brush ?? Ink, Margin + labelW, Base(Y, lineH, font));
            Y += lineH;
        }
        Y += 5;
    }

    /// <summary>Bulleted line(s): a small accent square, text wrapped beside it.</summary>
    public void Bullet(string text, XFont font, XBrush brush, double lineH = 13.5, XColor? dot = null)
    {
        var lines = Wrap(text, font, Width - 16);
        Ensure(Math.Min(2, lines.Count) * lineH);
        G.DrawRectangle(new XSolidBrush(dot ?? UI.ColAccent), Margin + 2, Y + lineH / 2 - 2, 4, 4);
        foreach (var line in lines)
        {
            Ensure(lineH);
            Text(line, font, brush, Margin + 16, Base(Y, lineH, font));
            Y += lineH;
        }
    }

    /// <summary>Tinted box with a coloured left bar: a title and a few wrapped lines (warnings, notes).</summary>
    public void Callout(string title, IReadOnlyList<string> lines, XColor accent, XSolidBrush titleBrush)
    {
        const double lineH = 12, pad = 9;
        double inner = Width - 2 * pad - 6;
        var titleLines = Wrap(title, UI.F8B, inner);
        var body = lines.Select(l => Wrap(l, UI.F8, inner - 10)).ToList();
        double h = pad * 2 + titleLines.Count * lineH + body.Sum(b => b.Count) * lineH + (body.Count > 0 ? 3 : 0);
        if (h > Bottom - Top) { foreach (var t in titleLines) Paragraph(t, UI.F8B, titleBrush); foreach (var l in lines) Bullet(l, UI.F8, Ink2, 12, accent); return; }
        Ensure(h + 4);
        G.DrawRectangle(new XSolidBrush(UI.Tint(accent, 0.92)), Margin, Y, Width, h);
        G.DrawRectangle(new XSolidBrush(accent), Margin, Y, 3, h);
        double y = Y + pad;
        foreach (var t in titleLines) { Text(t, UI.F8B, titleBrush, Margin + pad + 6, Base(y, lineH, UI.F8B)); y += lineH; }
        if (body.Count > 0) y += 3;
        foreach (var b in body)
            for (int k = 0; k < b.Count; k++)
            {
                if (k == 0) G.DrawEllipse(new XSolidBrush(accent), Margin + pad + 6, y + lineH / 2 - 1.5, 3, 3);
                Text(b[k], UI.F8, Ink2, Margin + pad + 16, Base(y, lineH, UI.F8));
                y += lineH;
            }
        Y += h + 10;
    }

    /// <summary>Label/value rows with hairlines (scan scope, dossier attributes, metadata).</summary>
    public void KeyValues(IEnumerable<(string Label, string Value, bool Warn)> rows, double labelW = 150)
    {
        const double lineH = 12.5;
        var warn = new XSolidBrush(UI.ColCritical);
        foreach (var (label, value, isWarn) in rows)
        {
            var lines = Wrap(value.Length == 0 ? "—" : value, UI.F8, Width - labelW - 8);
            double h = lines.Count * lineH + 7;
            Ensure(h);
            Text(label, UI.F8B, Muted, Margin, Base(Y + 3, lineH, UI.F8));
            for (int k = 0; k < lines.Count; k++)
                Text(lines[k], isWarn ? UI.F8B : UI.F8, isWarn ? warn : Ink, Margin + labelW, Base(Y + 3 + k * lineH, lineH, UI.F8));
            Y += h;
            G.DrawLine(new XPen(UI.ColBorder, 0.6), Margin, Y, Margin + Width, Y);
        }
        Y += 8;
    }

    // ── code ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Multi-line PowerShell (fix/undo, F-01): monospace on a light band with a slate bar, indentation kept,
    /// wrapped by <see cref="TextWrap.Code"/> so a line copied from the PDF either runs exactly like the source
    /// (backtick/pipe/comma continuation, comments stay comments) or fails loudly (» marker).
    /// Returns true when a line had to be wrapped.
    /// </summary>
    public bool Code(string code)
    {
        const double lineH = 10.5, pad = 5, inset = 10;
        double charW = W("MMMMMMMMMM", UI.Mono8) / 10;
        int cols = (int)Math.Floor((Width - 2 * inset) / Math.Max(1, charW));
        var lines = TextWrap.Code(code, cols);
        var band = new XSolidBrush(UI.ColSurface2);
        var bar  = new XSolidBrush(UI.ColBorderDark);
        void Band(double h) { G.DrawRectangle(band, Margin, Y, Width, h); G.DrawRectangle(bar, Margin, Y, 2, h); }
        Ensure(lineH * Math.Min(3, lines.Count) + 2 * pad);
        Band(pad); Y += pad;
        foreach (var line in lines)
        {
            if (Ensure(lineH + pad)) { Band(pad); Y += pad; }
            Band(lineH);
            Text(line.Text.Length == 0 ? " " : line.Text, UI.Mono8, line.IsContinuation ? Muted : Ink, Margin + inset, Y + 7.8);
            Y += lineH;
        }
        Band(pad); Y += pad + 8;
        return lines.Any(l => l.IsContinuation);
    }

    // ── tables ────────────────────────────────────────────────────────────────
    /// <summary>
    /// A table whose cells wrap (F-02): column widths from the model's weights or the content, every row as
    /// high as its longest cell, hairline rows; column headers repeat after every page break (S18-2).
    /// Six or more columns switch to a landscape page.
    /// </summary>
    /// <summary>
    /// Space a table should find on the current page before it starts: a short table as a whole, a long one
    /// with its first rows — so a heading or a small table never ends up alone at the bottom of a page.
    /// </summary>
    public static double KeepFor(ReportTable t) =>
        Math.Min(19 + 20 * Math.Max(1, t.Rows.Count) + (t.HiddenRows > 0 ? 18 : 0) + 6, 140);

    /// <param name="rowAccent">Optional colour per row index: the row gets a light tint and a 3 pt bar in it.</param>
    public void Table(ReportTable t, IReadOnlyList<CellKind>? kinds = null, string emptyText = "None.", Func<int, XColor?>? rowAccent = null)
    {
        if (t.Headers.Count >= 6 && !IsLandscape) NewPage(landscape: true);
        int cols = Math.Max(1, t.Headers.Count);
        CellKind KindOf(int c) => kinds is not null && c < kinds.Count ? kinds[c] : CellKind.Text;
        XFont FontOf(int c) => KindOf(c) switch { CellKind.Mono => UI.Mono8, CellKind.Severity or CellKind.CheckStatus => UI.F8B, _ => UI.F8 };
        const double padX = 6;
        var natural = Enumerable.Range(0, cols).Select(c =>
            Math.Max(W(t.Headers.Count > c ? t.Headers[c].ToUpperInvariant() : "", UI.F7B),
                     t.Rows.Select(r => c < r.Count ? W(r[c] ?? "", FontOf(c)) + (KindOf(c) == CellKind.Severity ? 9 : 0) : 0).DefaultIfEmpty(0).Max())
            + 2 * padX).ToList();
        var widths = TextWrap.ColumnWidths(natural, Width, t.Weights);
        var xs = new double[cols];
        for (int c = 1; c < cols; c++) xs[c] = xs[c - 1] + widths[c - 1];

        IReadOnlyList<string> Cell(string? text, XFont font, double width)
        {
            var lines = Wrap(text ?? "", font, width - 2 * padX);
            if (lines.Count <= MaxCellLines) return lines;
            var cut = lines.Take(MaxCellLines).ToList();
            cut[^1] = Fit(cut[^1] + " …", font, width - 2 * padX);
            return cut;
        }

        var headCells = Enumerable.Range(0, t.Headers.Count).Select(c => Cell(t.Headers[c].ToUpperInvariant(), UI.F7B, widths[c])).ToList();
        int headN = headCells.Count == 0 ? 1 : headCells.Max(l => l.Count);
        double headH = headN * 10 + 9;
        var headBand = new XSolidBrush(UI.ColSurface2);

        void HeaderRow()
        {
            G.DrawRectangle(headBand, Margin, Y, Width, headH);
            for (int c = 0; c < headCells.Count; c++)
                for (int k = 0; k < headCells[c].Count; k++)
                {
                    var s = headCells[c][k];
                    double x = KindOf(c) == CellKind.Number ? Margin + xs[c] + widths[c] - padX - W(s, UI.F7B) : Margin + xs[c] + padX;
                    Text(s, UI.F7B, Muted, x, Y + 13 + k * 10);
                }
            Y += headH;
        }

        if (t.Rows.Count == 0)
        {
            Ensure(headH + 20);
            HeaderRow();
            Text(emptyText, UI.F8, Muted, Margin + padX, Y + 13);
            Y += 26;
            return;
        }

        var line = new XPen(UI.ColBorder, 0.6);
        bool first = true;
        int rowNo = -1;
        foreach (var row in t.Rows)
        {
            rowNo++;
            var cells = Enumerable.Range(0, Math.Min(row.Count, t.Headers.Count))
                .Select(c => Cell(row[c], FontOf(c), widths[c] - (KindOf(c) == CellKind.Severity ? 9 : 0))).ToList();
            int n = cells.Count == 0 ? 1 : Math.Max(1, cells.Max(l => l.Count));
            double h = n * CellLineH + 9;
            if (first) { Ensure(Math.Max(headH + h, KeepFor(t))); HeaderRow(); first = false; }
            else if (Ensure(h)) HeaderRow();          // re-print column headers after the break
            if (rowAccent?.Invoke(rowNo) is { } ra)
            {
                G.DrawRectangle(new XSolidBrush(UI.Tint(ra, 0.93)), Margin, Y, Width, h);
                G.DrawRectangle(new XSolidBrush(ra), Margin, Y, 3, h);
            }
            for (int c = 0; c < cells.Count; c++)
            {
                var kind = KindOf(c);
                double x0 = Margin + xs[c] + padX;
                XBrush brush = kind == CellKind.Muted ? Muted : Ink;
                if (kind == CellKind.CheckStatus && cells[c].Count > 0)   // "Failed" red, "Found" amber, rest grey
                    brush = cells[c][0] switch
                    {
                        "Failed" => new XSolidBrush(UI.Shade(UI.ColCritical, 0.85)),
                        "Found"  => Ui.SeverityStyle(AD.Models.FindingSeverity.High).text,
                        "Passed" => Ink2,
                        _        => Muted,
                    };
                if (kind == CellKind.Severity && cells[c].Count > 0 && cells[c][0].Length > 0)
                {
                    var (_, accent, txt) = Ui.SeverityStyle(UI.ParseSeverity(cells[c][0]));
                    G.DrawEllipse(new XSolidBrush(accent), x0, Y + 7, 5, 5);
                    x0 += 9; brush = txt;
                }
                for (int k = 0; k < cells[c].Count; k++)
                {
                    var s = cells[c][k];
                    double x = kind == CellKind.Number ? Margin + xs[c] + widths[c] - padX - W(s, FontOf(c)) : x0;
                    Text(s, FontOf(c), brush, x, Y + 13 + k * CellLineH);
                }
            }
            Y += h;
            G.DrawLine(line, Margin, Y, Margin + Width, Y);
        }
        if (t.HiddenRows > 0) { Ensure(18); Text(t.HiddenHint, UI.F8B, Muted, Margin + padX, Y + 13); Y += 18; }
        Y += 10;
    }

    // ── footers with "Page x of y" (second pass) ─────────────────────────────
    /// <param name="beforeFooters">Second-pass drawing that needs the final page count (contents on the cover).</param>
    public void Finish(string filePath, string title, string footerLeft, Action? beforeFooters = null)
    {
        G?.Dispose();
        beforeFooters?.Invoke();
        int total = Doc.PageCount;
        for (int i = 0; i < total; i++)
        {
            var page = Doc.Pages[i];
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            double w = page.Width.Point, y0 = page.Height.Point - FooterH;
            g.DrawLine(new XPen(UI.ColBorder, 0.8), Margin, y0, w - Margin, y0);
            var left = footerLeft;
            _sink?.Invoke(left);                       // V-25: the footer reaches the text sink too
            var pg = ReportPdfRenderer.PageLabel(i + 1, total);
            _sink?.Invoke(pg);
            double pgW = g.MeasureString(pg, UI.F7B).Width;
            double avail = w - 2 * Margin - pgW - 16;
            var shown = left;
            for (var cut = left; cut.Length > 1 && g.MeasureString(shown, UI.F7).Width > avail; ) { cut = cut[..^1]; shown = cut + "…"; }
            g.DrawString(shown, UI.F7, Muted, Margin, y0 + 15);
            g.DrawString(pg, UI.F7B, Muted, w - Margin - pgW, y0 + 15);
        }
        Doc.Info.Title = title;
        Doc.Info.Creator = "DirHealth";
        Doc.Save(filePath);
    }
}
