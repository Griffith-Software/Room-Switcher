using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace RoomSwitcher.App.Ui;

/// <summary>Colours, fonts, icon glyphs and drawing helpers shared by the setup window.</summary>
internal static class Theme
{
    // Header band
    public static readonly Color HeaderTop = Color.FromArgb(12, 35, 76);
    public static readonly Color HeaderBottom = Color.FromArgb(30, 66, 120);
    public static readonly Color HeaderText = Color.White;
    public static readonly Color HeaderSubtext = Color.FromArgb(200, 214, 236);

    // Surfaces
    public static readonly Color Window = Color.FromArgb(243, 245, 249);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(222, 228, 236);
    public static readonly Color GridHeader = Color.FromArgb(236, 240, 246);
    public static readonly Color Selection = Color.FromArgb(214, 230, 251);
    public static readonly Color NavSelected = Color.FromArgb(228, 238, 252);
    public static readonly Color NavHover = Color.FromArgb(234, 239, 246);
    public static readonly Color ErrorRow = Color.FromArgb(253, 232, 232);

    // Text
    public static readonly Color Text = Color.FromArgb(22, 30, 45);
    public static readonly Color Subtext = Color.FromArgb(90, 99, 114);
    public static readonly Color Disabled = Color.FromArgb(160, 167, 178);

    // Accents
    public static readonly Color Accent = Color.FromArgb(16, 104, 230);
    public static readonly Color AccentHover = Color.FromArgb(12, 88, 200);
    public static readonly Color AccentPressed = Color.FromArgb(10, 74, 170);
    public static readonly Color NumberIdle = Color.FromArgb(226, 231, 238);
    public static readonly Color Success = Color.FromArgb(22, 128, 61);
    public static readonly Color SuccessBack = Color.FromArgb(222, 245, 228);
    public static readonly Color SuccessDot = Color.FromArgb(34, 170, 80);
    public static readonly Color Danger = Color.FromArgb(185, 28, 28);
    public static readonly Color NeutralBack = Color.FromArgb(234, 237, 242);
    public static readonly Color WarningBack = Color.FromArgb(254, 243, 214);
    public static readonly Color Warning = Color.FromArgb(146, 94, 0);

    private static readonly string TextFamily = Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    private static readonly string DisplayFamily = Installed("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
    private static readonly string GlyphFamily = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static Font Body { get; } = new(TextFamily, 10f);
    public static Font BodySemibold { get; } = new(Installed("Segoe UI Semibold") ? "Segoe UI Semibold" : TextFamily, 10f);
    public static Font Small { get; } = new(TextFamily, 9f);
    public static Font NavTitle { get; } = new(DisplayFamily, 11f, FontStyle.Bold);
    public static Font PageTitle { get; } = new(DisplayFamily, 18f, FontStyle.Bold);
    public static Font PageSubtitle { get; } = new(TextFamily, 10.5f);
    public static Font HeaderTitle { get; } = new(DisplayFamily, 17f, FontStyle.Bold);
    public static Font HeaderSubtitle { get; } = new(TextFamily, 10.5f);

    public static Font Glyph(float points) => new(GlyphFamily, points);

    /// <summary>Segoe Fluent Icons / MDL2 code points.</summary>
    public static class Glyphs
    {
        public const string Edit = "";
        public const string AllMonitors = "";
        public const string NextRoom = "";
        public const string Checkbox = "";
        public const string CheckboxChecked = "";
        public const string Test = "";
        public const string Info = "";
        public const string Play = "";
        public const string Stop = "";
        public const string Microphone = "";
        public const string Speaker = "";
        public const string Camera = "";
        public const string Usb = "";
        public const string Shield = "";
        public const string Monitor = "";
        public const string Rooms = "";
        public const string Arrange = "";
        public const string Settings = "";
        public const string Refresh = "";
        public const string Delete = "";
        public const string Back = "";
        public const string Forward = "";
        public const string Add = "";
        public const string Copy = "";
        public const string Up = "";
        public const string Down = "";
        public const string Save = "";
        public const string Power = "";
        public const string Check = "";
        public const string Minimize = "";
        public const string Maximize = "";
        public const string Restore = "";
        public const string Close = "";
    }

    private static bool Installed(string family)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase));
    }

    public static float Scale(Control c) => c.DeviceDpi / 96f;

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void DrawGlyph(Graphics g, string glyph, float points, Color color, RectangleF bounds)
    {
        using var font = Glyph(points);
        TextRenderer.DrawText(g, glyph, font, Rectangle.Round(bounds), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    /// <summary>The app icon artwork, for the header and the tray.</summary>
    public static Image AppImage { get; } = LoadAppImage();

    private static Image LoadAppImage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoomSwitcher.App.Assets.RoomSwitcher-512.png");
        return stream != null ? new Bitmap(stream) : new Bitmap(1, 1);
    }
}
