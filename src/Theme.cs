using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SilenceFill;

internal sealed record AppPalette(
    Color Background, Color Surface, Color Raised, Color Input, Color Border,
    Color Text, Color Muted, Color Accent, Color AccentText, Color AccentSoft,
    Color Plot, Color Grid, Color[] Quadrants, Color QuadrantText, Color Trail)
{
    public static readonly AppPalette Dark = new(
        Color.FromArgb(14, 22, 32), Color.FromArgb(23, 35, 49),
        Color.FromArgb(31, 47, 63), Color.FromArgb(30, 45, 60),
        Color.FromArgb(54, 77, 96), Color.FromArgb(234, 243, 247),
        Color.FromArgb(164, 184, 198), Color.FromArgb(92, 218, 196),
        Color.FromArgb(12, 35, 39), Color.FromArgb(35, 80, 82),
        Color.FromArgb(23, 35, 49), Color.FromArgb(75, 100, 120),
        [Color.FromArgb(39, 61, 94), Color.FromArgb(72, 48, 84),
         Color.FromArgb(28, 77, 73), Color.FromArgb(88, 66, 47)],
        Color.FromArgb(231, 241, 245), Color.FromArgb(139, 230, 232));

    public static readonly AppPalette Light = new(
        Color.FromArgb(242, 247, 248), Color.White,
        Color.FromArgb(230, 240, 242), Color.FromArgb(250, 253, 253),
        Color.FromArgb(195, 212, 219), Color.FromArgb(29, 47, 58),
        Color.FromArgb(88, 111, 123), Color.FromArgb(8, 119, 111),
        Color.White, Color.FromArgb(218, 240, 236),
        Color.White, Color.FromArgb(199, 217, 222),
        [Color.FromArgb(216, 228, 245), Color.FromArgb(237, 220, 242),
         Color.FromArgb(211, 238, 228), Color.FromArgb(249, 232, 207)],
        Color.FromArgb(39, 64, 75), Color.FromArgb(37, 123, 133));
}

internal static class AppTheme
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void ApplyTitleBar(Form form, AppPalette palette)
    {
        if (!form.IsHandleCreated || Environment.OSVersion.Version.Build < 22000) return;
        var dark = ReferenceEquals(palette, AppPalette.Dark) ? 1 : 0;
        var caption = palette.Background.R | palette.Background.G << 8 | palette.Background.B << 16;
        var text = palette.Text.R | palette.Text.G << 8 | palette.Text.B << 16;
        try
        {
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        }
        catch (Exception) { }
    }

    public static AppPalette Resolve(string choice)
    {
        if (choice == "Dark") return AppPalette.Dark;
        if (choice == "Light") return AppPalette.Light;
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int value && value == 0) return AppPalette.Dark;
        }
        catch (Exception) { }
        return AppPalette.Light;
    }

    public static void Apply(Control root, AppPalette palette)
    {
        foreach (Control child in root.Controls)
        {
            switch (child)
            {
                case ThemedTabControl tab:
                    tab.Palette = palette;
                    tab.BackColor = palette.Background;
                    tab.ForeColor = palette.Text;
                    Apply(tab, palette);
                    tab.Invalidate();
                    break;
                case DropSelect select:
                    select.Palette = palette;
                    select.Invalidate();
                    break;
                case ThemedStepper stepper:
                    stepper.Palette = palette;
                    stepper.Invalidate();
                    break;
                case AutomationToggle toggle:
                    toggle.Palette = palette;
                    break;
                case AppRow row:
                    row.Palette = palette;
                    break;
                case AppScrollIndicator indicator:
                    indicator.Palette = palette;
                    break;
                case Button button:
                    StyleButton(button, palette);
                    break;
                case CheckBox checkbox:
                    checkbox.BackColor = checkbox.Checked ? palette.Accent : palette.Raised;
                    checkbox.ForeColor = checkbox.Checked ? palette.AccentText : palette.Text;
                    checkbox.FlatAppearance.BorderColor = checkbox.Checked ? palette.Accent : palette.Border;
                    break;
                case TextBox box:
                    box.BackColor = palette.Input;
                    box.ForeColor = palette.Text;
                    box.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = palette.Input;
                    numeric.ForeColor = palette.Text;
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListView list:
                    list.BackColor = palette.Surface;
                    list.ForeColor = palette.Text;
                    list.BorderStyle = BorderStyle.None;
                    break;
                case Label label:
                    label.BackColor = Color.Transparent;
                    label.ForeColor = label.Tag as string == "muted" ? palette.Muted : palette.Text;
                    break;
                default:
                    child.BackColor = palette.Background;
                    child.ForeColor = palette.Text;
                    Apply(child, palette);
                    break;
            }
        }
    }

    public static void StyleButton(Button button, AppPalette palette)
    {
        var primary = button.Tag as string == "primary";
        var danger = button.Tag as string == "danger";
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = danger ? Color.FromArgb(162, 60, 63) : primary ? palette.Accent : palette.Raised;
        button.ForeColor = danger ? Color.White : primary ? palette.AccentText : palette.Text;
        button.FlatAppearance.BorderColor = danger ? button.BackColor : primary ? palette.Accent : palette.Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = danger ? Color.FromArgb(185, 69, 71) : primary ? palette.Accent : palette.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = palette.AccentSoft;
        button.Padding = new Padding(5, 2, 5, 2);
    }

    public static void StyleMenu(ContextMenuStrip menu, AppPalette palette)
    {
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(palette));
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = palette.Surface;
            item.ForeColor = palette.Text;
            if (item is ToolStripMenuItem submenu)
                foreach (ToolStripItem subitem in submenu.DropDownItems)
                {
                    subitem.BackColor = palette.Surface;
                    subitem.ForeColor = palette.Text;
                }
        }
    }

    public static DialogResult ShowMessage(Form owner, AppPalette palette, string title, string message,
        bool confirm = false)
    {
        using var dialog = new Form
        {
            Text = title, Size = new Size(460, 230), MinimumSize = new Size(400, 205),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false
        };
        var proceed = new Button { Text = confirm ? "Delete history" : "OK", Size = new Size(122, 32),
            DialogResult = confirm ? DialogResult.Yes : DialogResult.OK, Tag = confirm ? "danger" : "primary" };
        proceed.Location = new Point(dialog.ClientSize.Width - proceed.Width - 24, dialog.ClientSize.Height - 48);
        dialog.Controls.Add(proceed);
        dialog.AcceptButton = proceed;
        if (confirm)
        {
            var cancel = new Button { Text = "Cancel", Size = new Size(90, 32), DialogResult = DialogResult.No };
            cancel.Location = new Point(proceed.Left - cancel.Width - 10, proceed.Top);
            dialog.Controls.Add(cancel);
            dialog.CancelButton = cancel;
        }
        dialog.Controls.Add(new Label { Text = message, Location = new Point(30, 34),
            Size = new Size(dialog.ClientSize.Width - 60, dialog.ClientSize.Height - 110),
            TextAlign = ContentAlignment.MiddleLeft });
        dialog.BackColor = palette.Background;
        AppTheme.Apply(dialog, palette);
        dialog.Shown += (_, _) => ApplyTitleBar(dialog, palette);
        return dialog.ShowDialog(owner);
    }

    private sealed class MenuColors(AppPalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color ImageMarginGradientBegin => palette.Surface;
        public override Color ImageMarginGradientMiddle => palette.Surface;
        public override Color ImageMarginGradientEnd => palette.Surface;
        public override Color MenuItemSelected => palette.AccentSoft;
        public override Color MenuItemBorder => palette.Border;
        public override Color MenuBorder => palette.Border;
        public override Color MenuItemSelectedGradientBegin => palette.AccentSoft;
        public override Color MenuItemSelectedGradientEnd => palette.AccentSoft;
    }
}

internal sealed class DropSelect : Control
{
    private readonly ContextMenuStrip menu = new();
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette Palette { get; set; } = AppPalette.Light;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<object> Items { get; } = [];
    private int selectedIndex = -1;
    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = value >= 0 && value < Items.Count ? value : -1;
            Text = SelectedItem?.ToString() ?? "";
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;
        set => SelectedIndex = Items.FindIndex(x => Equals(x, value));
    }

    public DropSelect()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(135, 30);
        MinimumSize = new Size(80, 28);
        TabStop = true;
        AccessibleRole = AccessibleRole.ComboBox;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Palette.Input);
        using var border = new Pen(Focused ? Palette.Accent : Palette.Border);
        e.Graphics.FillRectangle(background, ClientRectangle);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, SelectedItem?.ToString() ?? "Choose…", Font,
            new Rectangle(10, 1, Math.Max(1, Width - 34), Height - 2), Palette.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var arrow = new Pen(Palette.Muted, 1.7f);
        var x = Width - 17;
        var y = Height / 2 - 2;
        e.Graphics.DrawLines(arrow, [new Point(x - 4, y), new Point(x, y + 4), new Point(x + 4, y)]);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); OpenMenu(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OpenMenu(); e.Handled = true; }
        else if (e.KeyCode == Keys.Down && Items.Count > 0) { SelectedIndex = Math.Min(Items.Count - 1, selectedIndex + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Up && Items.Count > 0) { SelectedIndex = Math.Max(0, selectedIndex - 1); e.Handled = true; }
        base.OnKeyDown(e);
    }

    private void OpenMenu()
    {
        if (Items.Count == 0) return;
        if (menu.Visible) { menu.Close(); return; }
        menu.Items.Clear();
        for (var i = 0; i < Items.Count; i++)
        {
            var index = i;
            menu.Items.Add(new ToolStripMenuItem(Items[i]?.ToString() ?? "") { Checked = i == selectedIndex });
            menu.Items[^1].Click += (_, _) => SelectedIndex = index;
        }
        AppTheme.StyleMenu(menu, Palette);
        menu.Show(this, 0, Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) menu.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ThemedStepper : UserControl
{
    private readonly TextBox entry = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center };
    private decimal value;
    private decimal minimum;
    private decimal maximum = 100;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            entry.BackColor = value.Input;
            entry.ForeColor = value.Text;
            Invalidate();
        }
    }
    private AppPalette palette = AppPalette.Light;
    public event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Minimum { get => minimum; set { minimum = value; Value = this.value; } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Maximum { get => maximum; set { maximum = value; Value = this.value; } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, minimum, maximum);
            if (this.value == next) { entry.Text = next.ToString(); return; }
            this.value = next;
            entry.Text = next.ToString();
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public ThemedStepper()
    {
        Size = new Size(70, 30);
        MinimumSize = new Size(58, 28);
        AccessibleRole = AccessibleRole.SpinButton;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        entry.Text = "0";
        entry.AccessibleName = "Numeric value";
        entry.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Up) { Value++; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Value--; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { CommitEntry(); e.SuppressKeyPress = true; }
        };
        entry.Leave += (_, _) => CommitEntry();
        Controls.Add(entry);
        Resize += (_, _) => PositionEntry();
        PositionEntry();
    }

    private void PositionEntry() => entry.SetBounds(5, Math.Max(2, (Height - entry.PreferredHeight) / 2), Math.Max(15, Width - 30), entry.PreferredHeight);
    private void CommitEntry()
    {
        if (decimal.TryParse(entry.Text, out var parsed)) Value = parsed;
        else entry.Text = value.ToString();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.X > Width - 25) Value += e.Y < Height / 2 ? 1 : -1;
        else entry.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Palette.Input);
        using var border = new Pen(Palette.Border);
        using var glyph = new Pen(Palette.Muted, 1.4f);
        e.Graphics.FillRectangle(background, ClientRectangle);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var x = Width - 13;
        e.Graphics.DrawLine(glyph, Width - 24, 1, Width - 24, Height - 2);
        e.Graphics.DrawLine(glyph, Width - 24, Height / 2, Width - 1, Height / 2);
        e.Graphics.DrawLine(glyph, x - 4, Height / 4, x + 4, Height / 4);
        e.Graphics.DrawLine(glyph, x, Height / 4 - 4, x, Height / 4 + 4);
        e.Graphics.DrawLine(glyph, x - 4, Height * 3 / 4, x + 4, Height * 3 / 4);
    }
}

internal sealed class ThemedTabControl : UserControl
{
    private readonly HeaderPanel header = new() { Dock = DockStyle.Top, Height = 36, TabStop = true,
        AccessibleName = "SilenceFill sections", AccessibleRole = AccessibleRole.PageTabList };
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private AppPalette palette = AppPalette.Light;
    private int selectedIndex = -1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            BackColor = value.Background;
            header.BackColor = value.Background;
            content.BackColor = value.Background;
            foreach (var page in TabPages) page.Page.BackColor = value.Background;
            header.Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<(string Title, Control Page)> TabPages { get; } = [];
    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= TabPages.Count || value == selectedIndex) return;
            if (selectedIndex >= 0) TabPages[selectedIndex].Page.Visible = false;
            selectedIndex = value;
            TabPages[selectedIndex].Page.Visible = true;
            TabPages[selectedIndex].Page.BringToFront();
            header.AccessibleName = $"SilenceFill sections. {TabPages[selectedIndex].Title} selected. Use left and right arrows to change sections.";
            header.Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ThemedTabControl()
    {
        Controls.Add(content);
        Controls.Add(header);
        header.Paint += (_, e) => DrawHeader(e.Graphics);
        header.MouseDown += (_, e) =>
        {
            var index = e.X / 126;
            if (index >= 0 && index < TabPages.Count) SelectedIndex = index;
        };
        header.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Right) { SelectedIndex = Math.Min(TabPages.Count - 1, selectedIndex + 1); e.Handled = true; }
            if (e.KeyCode == Keys.Left) { SelectedIndex = Math.Max(0, selectedIndex - 1); e.Handled = true; }
        };
    }

    public void AddPages(params (string Title, Control Page)[] pages)
    {
        foreach (var page in pages)
        {
            page.Page.BackColor = Palette.Background;
            page.Page.Dock = DockStyle.Fill;
            TabPages.Add(page);
            content.Controls.Add(page.Page);
            page.Page.Visible = false;
        }
        if (selectedIndex < 0 && TabPages.Count > 0) SelectedIndex = 0;
        header.Invalidate();
    }

    private void DrawHeader(Graphics graphics)
    {
        graphics.Clear(Palette.Background);
        for (var i = 0; i < TabPages.Count; i++)
        {
            var bounds = new Rectangle(i * 126, 0, 126, header.Height);
            using var background = new SolidBrush(i == SelectedIndex ? Palette.Surface : Palette.Background);
            graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(graphics, TabPages[i].Title, Font, bounds,
                i == SelectedIndex ? Palette.Accent : Palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (i == SelectedIndex)
            {
                using var underline = new SolidBrush(Palette.Accent);
                graphics.FillRectangle(underline, bounds.Left + 16, bounds.Bottom - 3, bounds.Width - 32, 3);
            }
        }
    }

    private sealed class HeaderPanel : Panel
    {
        public HeaderPanel() => DoubleBuffered = true;
    }
}
