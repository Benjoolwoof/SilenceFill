using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SilenceFill;

internal sealed class AutomationToggle : Control
{
    private int P(int value) => DpiScale.Pixels(this, value);
    private bool isOn;
    private AppPalette palette = AppPalette.Light;
    public event EventHandler? Toggled;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsOn
    {
        get => isOn;
        set { isOn = value; AccessibleName = $"Automation {(value ? "on" : "paused")}. Press Space to change."; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette Palette { get => palette; set { palette = value; Invalidate(); } }

    public AutomationToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(222, 38);
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        IsOn = !IsOn;
        Toggled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(e.Graphics, "Automation", Font, new Rectangle(0, 0, P(102), Height), Palette.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        var track = new Rectangle(P(105), (Height - P(24)) / 2, P(44), P(24));
        using var trackPath = Rounded(track, P(12));
        using var fill = new SolidBrush(IsOn ? Palette.Accent : Palette.Border);
        e.Graphics.FillPath(fill, trackPath);
        using var thumb = new SolidBrush(IsOn ? Palette.AccentText : Palette.Surface);
        e.Graphics.FillEllipse(thumb, IsOn ? track.Right - P(21) : track.Left + P(3), track.Top + P(3), P(18), P(18));
        TextRenderer.DrawText(e.Graphics, IsOn ? "On" : "Paused", Font,
            new Rectangle(P(157), 0, Math.Max(1, Width - P(157)), Height), IsOn ? Palette.Accent : Palette.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused)
        {
            using var focusPath = Rounded(new Rectangle(track.Left - P(3), track.Top - P(3), track.Width + P(6), track.Height + P(6)), P(14));
            using var focusPen = new Pen(Palette.Accent, 1.5f * DeviceDpi / 96f);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class AppRow : Control
{
    private int P(int value) => DpiScale.Pixels(this, value);
    private bool selected;
    private bool hovered;
    private AppPalette palette = AppPalette.Light;
    public AppChoice Choice { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? AppIcon { get; init; }
    public event EventHandler? Toggled;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set { selected = value; UpdateAccessibility(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette Palette { get => palette; set { palette = value; Invalidate(); } }

    public AppRow(AppChoice choice)
    {
        Choice = choice;
        Height = 54;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateAccessibility();
    }

    private void UpdateAccessibility() => AccessibleName = $"{Choice.DisplayName}, {(selected ? "monitored" : "not monitored")}, {(Choice.IsRunning ? "open now" : "not open")}. Press Space to change.";

    protected override void OnClick(EventArgs e) { base.OnClick(e); Toggled?.Invoke(this, EventArgs.Empty); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var card = new Rectangle(0, 0, Width - P(1), Height - P(1));
        using (var shape = Rounded(card, P(8)))
        using (var background = new SolidBrush(selected ? Palette.Raised : hovered ? Palette.AccentSoft : Palette.Surface))
        using (var outline = new Pen(selected ? Palette.Accent : Palette.Border, P(1)))
        {
            e.Graphics.FillPath(background, shape);
            e.Graphics.DrawPath(outline, shape);
        }
        if (AppIcon is not null) e.Graphics.DrawImage(AppIcon, new Rectangle(P(16), (Height - P(22)) / 2, P(22), P(22)));
        var textRight = Math.Max(P(90), Width - P(108));
        var titleHeight = Math.Max(P(23), Font.Height);
        var subtitleHeight = Math.Max(P(19), Font.Height);
        var textTop = (Height - titleHeight - P(2) - subtitleHeight) / 2;
        TextRenderer.DrawText(e.Graphics, Choice.DisplayName, Font,
            new Rectangle(P(50), textTop, Math.Max(1, textRight - P(54)), titleHeight), Palette.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        var subtitle = Choice.DisplayName.Equals(Choice.Name, StringComparison.OrdinalIgnoreCase)
            ? Choice.IsRunning ? "Open now" : "Not open right now"
            : $"{Choice.Name}  ·  {(Choice.IsRunning ? "Open now" : "Not open")}";
        TextRenderer.DrawText(e.Graphics, subtitle, Font,
            new Rectangle(P(50), textTop + titleHeight + P(2), Math.Max(1, textRight - P(54)), subtitleHeight), Palette.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        var badgeHeight = Math.Max(P(28), Font.Height + P(4));
        var badge = new Rectangle(Math.Max(Width - P(100), P(68)), (Height - badgeHeight) / 2, P(84), badgeHeight);
        using (var shape = Rounded(badge, P(7)))
        using (var brush = new SolidBrush(selected ? Palette.AccentSoft : Palette.Input))
            e.Graphics.FillPath(brush, shape);
        TextRenderer.DrawText(e.Graphics, selected ? "Monitoring" : "Add app", Font,
            badge, selected ? Palette.Accent : Palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(P(3), P(3), Math.Max(1, Width - P(7)), Math.Max(1, Height - P(7))), Palette.Accent, Palette.Surface);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class AppScrollIndicator : Control
{
    private int P(int value) => DpiScale.Pixels(this, value);
    private AppPalette palette = AppPalette.Light;
    private double position;
    private double visibleShare = 1;
    public event Action<double>? PositionRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppPalette Palette { get => palette; set { palette = value; Invalidate(); } }

    public AppScrollIndicator()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Width = 10;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = "App list position";
    }

    public void SetPosition(double fraction, double share)
    {
        position = Math.Clamp(fraction, 0, 1);
        visibleShare = Math.Clamp(share, 0.05, 1);
        Visible = visibleShare < 0.995;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) MoveTo(e.Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left) MoveTo(e.Y);
    }

    private void MoveTo(int y)
    {
        var thumb = Math.Max(P(25), (int)(Height * visibleShare));
        PositionRequested?.Invoke(Math.Clamp((double)(y - thumb / 2) / Math.Max(1, Height - thumb), 0, 1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var rail = new SolidBrush(Palette.Border);
        using var thumbBrush = new SolidBrush(Palette.Accent);
        e.Graphics.FillRectangle(rail, Width / 2 - P(2), P(4), P(4), Math.Max(1, Height - P(8)));
        var thumb = Math.Max(P(25), (int)(Height * visibleShare));
        var top = (int)((Height - thumb) * position);
        e.Graphics.FillRectangle(thumbBrush, Width / 2 - P(3), top, P(6), thumb);
    }
}
