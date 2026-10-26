namespace RoomSwitcher.App.Ui;

/// <summary>
/// Makes text boxes in a window behave like a form field: Enter, or a click anywhere outside the
/// box, keeps what was typed and takes the cursor away. Covers plain text boxes, text being typed
/// into a grid cell, number boxes, and keyboard shortcut boxes (where Enter means "done").
/// </summary>
internal sealed class TextCommit : IMessageFilter, IDisposable
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int VK_RETURN = 0x0D;

    private readonly Form _form;

    public TextCommit(Form form)
    {
        _form = form;
        Application.AddMessageFilter(this);
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is not (WM_KEYDOWN or WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_NCLBUTTONDOWN)) return false;
        var target = Control.FromChildHandle(m.HWnd);
        if (target?.FindForm() != _form && m.HWnd != _form.Handle) return false;

        var editing = Editing();
        if (editing == null) return false;

        if (m.Msg == WM_KEYDOWN)
        {
            if ((int)m.WParam != VK_RETURN || editing is TextBox { Multiline: true, AcceptsReturn: true }) return false;
            Commit(editing);
            return true; // the Enter is used up: no moving to the next row, no default button
        }

        // A click: inside the box being edited, let it through; anywhere else, finish editing first.
        if (target != null && (target == editing || editing.Contains(target))) return false;
        Commit(editing, keepFocusFor: target);
        return false;
    }

    /// <summary>The text box (or grid cell editor, or number box) that has the cursor, if any.</summary>
    private Control? Editing()
    {
        var focused = _form.ActiveControl;
        while (focused is ContainerControl { ActiveControl: { } inner } && focused is not NumericUpDown) focused = inner;
        return focused switch
        {
            TextBox or NumericUpDown or HotkeyBox => focused,
            _ when focused?.Parent is NumericUpDown up => up,
            _ => null,
        };
    }

    private void Commit(Control editing, Control? keepFocusFor = null)
    {
        // Text typed into a grid cell: save it into the cell (raises CellValueChanged).
        if (editing is IDataGridViewEditingControl { EditingControlDataGridView: { } grid })
        {
            grid.EndEdit();
            grid.CurrentCell = null;
        }
        else if (editing is NumericUpDown number)
        {
            number.Validate();
        }

        // Only drop the focus when the click wasn't on something that takes focus itself (a button,
        // a list, another text box): then the cursor simply goes away.
        if (keepFocusFor is { CanFocus: true, TabStop: true }) return;
        _form.ActiveControl = null;
        _form.Focus();
    }

    public void Dispose() => Application.RemoveMessageFilter(this);
}
