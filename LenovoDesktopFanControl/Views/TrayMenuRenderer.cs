using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace LenovoDesktopFanControl.Views;

internal sealed class TrayMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    private static readonly Drawing.Color Surface = Drawing.Color.FromArgb(18, 25, 35);
    private static readonly Drawing.Color SurfaceHover = Drawing.Color.FromArgb(32, 44, 58);
    private static readonly Drawing.Color Border = Drawing.Color.FromArgb(40, 54, 71);
    private static readonly Drawing.Color Accent = Drawing.Color.FromArgb(91, 157, 255);
    private static readonly Drawing.Color TextPrimary = Drawing.Color.FromArgb(244, 247, 251);
    private static readonly Drawing.Color TextSecondary = Drawing.Color.FromArgb(169, 181, 197);
    private static readonly Drawing.Color TextMuted = Drawing.Color.FromArgb(113, 128, 148);

    public TrayMenuRenderer() : base(new TrayMenuColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
    {
        var bounds = new Drawing.Rectangle(Drawing.Point.Empty, e.Item.Size);
        using var background = new Drawing.SolidBrush(e.Item.Selected ? SurfaceHover : Surface);
        e.Graphics.FillRectangle(background, bounds);

        if (!e.Item.Selected)
            return;

        var highlightBounds = new Drawing.Rectangle(1, 1, bounds.Width - 3, bounds.Height - 3);
        using var border = new Drawing.Pen(Accent);
        e.Graphics.DrawRectangle(border, highlightBounds);
    }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled
            ? TextMuted
            : e.Item.Selected ? TextPrimary : TextSecondary;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Drawing.Pen(Border);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
    {
        var bounds = new Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var pen = new Drawing.Pen(Border);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    private sealed class TrayMenuColorTable : WinForms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => Surface;
        public override Drawing.Color MenuItemSelected => SurfaceHover;
        public override Drawing.Color MenuItemBorder => Accent;
        public override Drawing.Color ImageMarginGradientBegin => Surface;
        public override Drawing.Color ImageMarginGradientMiddle => Surface;
        public override Drawing.Color ImageMarginGradientEnd => Surface;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Border;
    }
}
