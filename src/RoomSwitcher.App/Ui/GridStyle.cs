namespace RoomSwitcher.App.Ui;

/// <summary>Gives a DataGridView the setup window's look, and draws its special cells.</summary>
internal static class GridStyle
{
    public static void Apply(DataGridView grid)
    {
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Theme.Card;
        grid.GridColor = Theme.Border;
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleVertical;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowHeadersVisible = false;
        grid.AllowUserToResizeRows = false;
        grid.Font = Theme.Body;

        var header = grid.ColumnHeadersDefaultCellStyle;
        header.BackColor = Theme.GridHeader;
        header.ForeColor = Theme.Text;
        header.SelectionBackColor = Theme.GridHeader;
        header.SelectionForeColor = Theme.Text;
        header.Font = Theme.BodySemibold;
        header.Padding = new Padding(14, 0, 8, 0);
        header.WrapMode = DataGridViewTriState.True;

        var cells = grid.DefaultCellStyle;
        cells.BackColor = Theme.Card;
        cells.ForeColor = Theme.Text;
        cells.SelectionBackColor = Theme.Selection;
        cells.SelectionForeColor = Theme.Text;
        cells.Padding = new Padding(14, 0, 8, 0);
        cells.Font = Theme.Body;

        grid.DpiChangedAfterParent += (_, _) => Size(grid);
        grid.HandleCreated += (_, _) => Size(grid);
    }

    /// <summary>Row and header heights at the grid's current DPI.</summary>
    public static void Size(DataGridView grid, int rowHeight = 60, int headerHeight = 58)
    {
        var s = Theme.Scale(grid);
        grid.ColumnHeadersHeight = (int)(headerHeight * s);
        grid.RowTemplate.Height = (int)(rowHeight * s);
        foreach (DataGridViewRow row in grid.Rows) row.Height = (int)(rowHeight * s);
    }

    /// <summary>Paints the cell background and borders, but not its content.</summary>
    public static void PaintBackground(DataGridViewCellPaintingEventArgs e, Color? back = null)
    {
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        using (var b = new SolidBrush(back ?? (selected ? e.CellStyle!.SelectionBackColor : e.CellStyle!.BackColor)))
            e.Graphics!.FillRectangle(b, e.CellBounds);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
    }

    /// <summary>A filled circle with a number in it.</summary>
    public static void PaintBadge(DataGridViewCellPaintingEventArgs e, string text, Color fill)
    {
        var g = e.Graphics!;
        Theme.Smooth(g);
        var s = e.CellBounds.Height / 60f;
        var d = 34 * s;
        var circle = new RectangleF(e.CellBounds.X + (e.CellBounds.Width - d) / 2, e.CellBounds.Y + (e.CellBounds.Height - d) / 2, d, d);
        using (var b = new SolidBrush(fill)) g.FillEllipse(b, circle);
        TextRenderer.DrawText(g, text, Theme.BodySemibold, Rectangle.Round(circle), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>The rounded white box an editable cell shows, so it reads as a text field.</summary>
    public static Rectangle InputRect(Rectangle cell)
    {
        var s = cell.Height / 60f;
        return new Rectangle(cell.X + (int)(14 * s), cell.Y + (int)(9 * s), cell.Width - (int)(34 * s), cell.Height - (int)(19 * s));
    }

    public static void PaintInput(DataGridViewCellPaintingEventArgs e, string text, bool enabled)
    {
        var g = e.Graphics!;
        Theme.Smooth(g);
        var r = InputRect(e.CellBounds);
        var s = e.CellBounds.Height / 60f;
        using var path = Theme.RoundedRect(r, 6 * s);
        using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
        using (var p = new Pen(Color.FromArgb(205, 212, 222))) g.DrawPath(p, path);
        var textRect = Rectangle.Inflate(r, -(int)(12 * s), 0);
        TextRenderer.DrawText(g, text, Theme.Body, textRect, enabled ? Theme.Text : Theme.Disabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    /// <summary>A rounded status pill with a coloured dot, like "● On".</summary>
    public static void PaintPill(DataGridViewCellPaintingEventArgs e, string text, Color back, Color fore, Color dot)
    {
        var g = e.Graphics!;
        Theme.Smooth(g);
        var s = e.CellBounds.Height / 60f;
        var textSize = TextRenderer.MeasureText(g, text, Theme.BodySemibold, System.Drawing.Size.Empty, TextFormatFlags.NoPadding);
        var h = 32 * s;
        var w = textSize.Width + 40 * s;
        var r = new RectangleF(e.CellBounds.X + 14 * s, e.CellBounds.Y + (e.CellBounds.Height - h) / 2, w, h);
        using var path = Theme.RoundedRect(r, 8 * s);
        using (var b = new SolidBrush(back)) g.FillPath(b, path);
        var dd = 9 * s;
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.X + 12 * s, r.Y + (h - dd) / 2, dd, dd);
        TextRenderer.DrawText(g, text, Theme.BodySemibold, new Rectangle((int)(r.X + 28 * s), (int)r.Y, textSize.Width + 4, (int)h), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>A routing cell: a filled blue circle with a white dot when routed, an empty ring otherwise.</summary>
    public static void PaintRoute(DataGridViewCellPaintingEventArgs e, bool routed, bool hover, bool enabled)
    {
        var g = e.Graphics!;
        Theme.Smooth(g);
        var s = e.CellBounds.Height / 60f;
        var d = 24 * s;
        var r = new RectangleF(e.CellBounds.X + (e.CellBounds.Width - d) / 2, e.CellBounds.Y + (e.CellBounds.Height - d) / 2, d, d);
        if (routed)
        {
            using var b = new SolidBrush(enabled ? Theme.Accent : Theme.Disabled);
            g.FillEllipse(b, r);
            var inner = RectangleF.Inflate(r, -7.5f * s, -7.5f * s);
            g.FillEllipse(Brushes.White, inner);
        }
        else
        {
            if (hover)
            {
                using var hb = new SolidBrush(Theme.NavSelected);
                g.FillEllipse(hb, RectangleF.Inflate(r, 5 * s, 5 * s));
            }
            using var p = new Pen(hover ? Theme.Accent : Color.FromArgb(178, 188, 202), 1.6f * s);
            g.DrawEllipse(p, r);
        }
    }

    /// <summary>A check box (a square: any number can be ticked), centred in the cell.</summary>
    public static void PaintCheck(DataGridViewCellPaintingEventArgs e, bool on, bool hover, bool enabled)
    {
        var g = e.Graphics!;
        Theme.Smooth(g);
        var s = e.CellBounds.Height / 60f;
        var d = 24 * s;
        var r = new RectangleF(e.CellBounds.X + (e.CellBounds.Width - d) / 2, e.CellBounds.Y + (e.CellBounds.Height - d) / 2, d, d);
        if (hover && !on)
        {
            using var hb = new SolidBrush(Theme.NavSelected);
            using var halo = Theme.RoundedRect(RectangleF.Inflate(r, 5 * s, 5 * s), 8 * s);
            g.FillPath(hb, halo);
        }
        using var box = Theme.RoundedRect(r, 5 * s);
        if (on)
        {
            using var b = new SolidBrush(enabled ? Theme.Accent : Theme.Disabled);
            g.FillPath(b, box);
            Theme.DrawGlyph(g, Theme.Glyphs.Check, 11f * s, Color.White, r);
        }
        else
        {
            using var p = new Pen(hover ? Theme.Accent : Color.FromArgb(178, 188, 202), 1.6f * s);
            g.DrawPath(p, box);
        }
    }

    /// <summary>A small square outlined button with just an icon (e.g. the trash can), centred in the cell.</summary>
    public static void PaintIconButton(DataGridViewCellPaintingEventArgs e, string glyph, bool hover)
    {
        PaintBackground(e);
        var g = e.Graphics!;
        Theme.Smooth(g);
        var s = e.CellBounds.Height / 60f;
        var d = 34 * s;
        var r = new RectangleF(e.CellBounds.X + (e.CellBounds.Width - d) / 2, e.CellBounds.Y + (e.CellBounds.Height - d) / 2, d, d);
        using var path = Theme.RoundedRect(r, 7 * s);
        using (var b = new SolidBrush(hover ? Theme.NavSelected : Theme.Card)) g.FillPath(b, path);
        using (var p = new Pen(hover ? Theme.Accent : Color.FromArgb(208, 215, 225))) g.DrawPath(p, path);
        Theme.DrawGlyph(g, glyph, 10f, hover ? Theme.Accent : Theme.Subtext, r);
    }

    /// <summary>A small outlined button drawn in a cell (the cell is a DataGridViewButtonCell, so it stays accessible).</summary>
    public static void PaintCellButton(DataGridViewCellPaintingEventArgs e, string text, string glyph, bool enabled, bool active = false)
    {
        PaintBackground(e);
        if (string.IsNullOrEmpty(text)) return;
        var g = e.Graphics!;
        Theme.Smooth(g);
        var s = e.CellBounds.Height / 60f;
        var size = TextRenderer.MeasureText(g, text, Theme.BodySemibold, System.Drawing.Size.Empty, TextFormatFlags.NoPadding);
        var w = size.Width + 52 * s;
        var h = 34 * s;
        var r = new RectangleF(e.CellBounds.X + 14 * s, e.CellBounds.Y + (e.CellBounds.Height - h) / 2, Math.Min(w, e.CellBounds.Width - 24 * s), h);
        using var path = Theme.RoundedRect(r, 7 * s);
        var fill = active ? Theme.Accent : Theme.Card;
        var fore = !enabled ? Theme.Disabled : active ? Color.White : Theme.Text;
        var glyphColor = !enabled ? Theme.Disabled : active ? Color.White : Theme.Accent;
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(active ? Theme.Accent : enabled ? Color.FromArgb(208, 215, 225) : Theme.Border)) g.DrawPath(p, path);
        Theme.DrawGlyph(g, glyph, 10f, glyphColor, new RectangleF(r.X + 8 * s, r.Y, 22 * s, r.Height));
        // Kept inside the button: a narrow column shortens the text rather than letting it spill out.
        var textRect = new Rectangle((int)(r.X + 32 * s), (int)r.Y, Math.Max(0, (int)(r.Right - 8 * s) - (int)(r.X + 32 * s)), (int)r.Height);
        TextRenderer.DrawText(g, text, Theme.BodySemibold, textRect, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    /// <summary>A horizontal level meter (0 to 1), green then amber near the top.</summary>
    public static void PaintMeter(DataGridViewCellPaintingEventArgs e, float level, bool active)
    {
        PaintBackground(e);
        var g = e.Graphics!;
        Theme.Smooth(g);
        var s = e.CellBounds.Height / 60f;
        var r = new RectangleF(e.CellBounds.X + 14 * s, e.CellBounds.Y + (e.CellBounds.Height - 10 * s) / 2, e.CellBounds.Width - 28 * s, 10 * s);
        using (var track = Theme.RoundedRect(r, 5 * s))
        using (var b = new SolidBrush(Theme.NeutralBack))
            g.FillPath(b, track);
        if (!active) return;
        // Perceived loudness: speech peaks are small numbers, so lift them with a square root.
        var shown = (float)Math.Sqrt(Math.Clamp(level, 0, 1));
        if (shown <= 0.01f) return;
        var filled = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * shown), r.Height);
        using var bar = Theme.RoundedRect(filled, 5 * s);
        using var fillBrush = new SolidBrush(shown > 0.85f ? Color.FromArgb(230, 150, 20) : Theme.SuccessDot);
        g.FillPath(fillBrush, bar);
    }

    /// <summary>
    /// A single click on a nickname box always opens it for typing, with the cursor at the end,
    /// even if that cell was already the grid's current cell (when it otherwise wouldn't).
    /// </summary>
    public static void ClickToEdit(DataGridView grid, string columnName)
    {
        grid.CellMouseDown += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Button != MouseButtons.Left) return;
            if (grid.Columns[e.ColumnIndex].Name != columnName || grid.Columns[e.ColumnIndex].ReadOnly) return;
            grid.BeginInvoke(() =>
            {
                if (grid.IsDisposed || e.RowIndex >= grid.Rows.Count) return;
                var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (grid.CurrentCell != cell) grid.CurrentCell = cell;
                if (!grid.IsCurrentCellInEditMode) grid.BeginEdit(false);
                if (grid.EditingControl is TextBox box) box.SelectionStart = box.TextLength;
            });
        };
    }
}
