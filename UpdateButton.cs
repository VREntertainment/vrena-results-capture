using System.Drawing.Drawing2D;

namespace VRenaResultsCapture;

internal sealed class UpdateButton : Button
{
    private readonly System.Windows.Forms.Timer _animationTimer;
    private float _animationProgress;

    internal bool UpdateAvailable { get; private set; }

    internal UpdateButton()
    {
        AutoSize = true;
        Padding = new Padding(8, 5, 8, 5);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.BorderColor = Color.FromArgb(178, 181, 194);
        BackColor = Color.White;
        ForeColor = Color.FromArgb(52, 55, 70);
        Text = "Check for updates";
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);

        _animationTimer = new System.Windows.Forms.Timer
        {
            Interval = 32
        };
        _animationTimer.Tick += (_, _) =>
        {
            _animationProgress = (_animationProgress + 0.0125f) % 1f;
            Invalidate();
        };
    }

    internal void SetUpdateAvailable(bool available)
    {
        UpdateAvailable = available;
        Enabled = true;
        Text = available ? "New update available" : "Check for updates";
        BackColor = available
            ? Color.FromArgb(244, 241, 255)
            : Color.White;
        ForeColor = available
            ? Color.FromArgb(76, 48, 220)
            : Color.FromArgb(52, 55, 70);
        FlatAppearance.BorderColor = available
            ? Color.FromArgb(103, 76, 236)
            : Color.FromArgb(178, 181, 194);

        if (available)
        {
            _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
            _animationProgress = 0;
        }

        Invalidate();
    }

    internal void SetBusy(string text)
    {
        _animationTimer.Stop();
        Text = text;
        Enabled = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs paintEvent)
    {
        base.OnPaint(paintEvent);
        if (!UpdateAvailable || !Enabled || ClientSize.Width < 8 || ClientSize.Height < 8)
        {
            return;
        }

        paintEvent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var border = new RectangleF(2, 2, ClientSize.Width - 5, ClientSize.Height - 5);
        for (var index = 7; index >= 0; index--)
        {
            var progress = (_animationProgress - index * 0.012f + 1f) % 1f;
            var point = PointOnBorder(border, progress);
            var opacity = Math.Max(20, 235 - index * 28);
            var radius = Math.Max(1.5f, 4.6f - index * 0.42f);
            using var glow = new SolidBrush(Color.FromArgb(opacity, 146, 225, 255));
            paintEvent.Graphics.FillEllipse(
                glow,
                point.X - radius,
                point.Y - radius,
                radius * 2,
                radius * 2);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private static PointF PointOnBorder(RectangleF border, float progress)
    {
        var perimeter = 2f * (border.Width + border.Height);
        var distance = progress * perimeter;
        if (distance <= border.Width)
        {
            return new PointF(border.Left + distance, border.Top);
        }

        distance -= border.Width;
        if (distance <= border.Height)
        {
            return new PointF(border.Right, border.Top + distance);
        }

        distance -= border.Height;
        if (distance <= border.Width)
        {
            return new PointF(border.Right - distance, border.Bottom);
        }

        distance -= border.Width;
        return new PointF(border.Left, border.Bottom - distance);
    }
}
