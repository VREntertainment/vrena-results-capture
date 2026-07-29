using System.Drawing.Imaging;

namespace VRenaResultsCapture;

internal sealed class ReferenceSelectionForm : Form
{
    private readonly Screen _screen;
    private readonly Bitmap _screenshot;
    private Point _selectionStart;
    private Rectangle _selection;
    private bool _isSelecting;

    internal Rectangle SelectedArea { get; private set; }
    internal Bitmap? SelectedImage { get; private set; }

    internal ReferenceSelectionForm(Screen screen)
    {
        _screen = screen;
        _screenshot = ScreenshotHelper.CaptureScreen(screen);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        ShowInTaskbar = false;
        TopMost = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black;

        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.DrawImage(_screenshot, ClientRectangle);

        using var shade = new SolidBrush(Color.FromArgb(105, Color.Black));
        eventArgs.Graphics.FillRectangle(shade, ClientRectangle);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            var sourceRectangle = ClientToImageRectangle(_selection);
            eventArgs.Graphics.DrawImage(_screenshot, _selection, sourceRectangle, GraphicsUnit.Pixel);

            using var border = new Pen(Color.FromArgb(255, 90, 220), 3);
            eventArgs.Graphics.DrawRectangle(border, _selection);
        }

        var instructionRectangle = new Rectangle(24, 24, Math.Min(720, ClientSize.Width - 48), 78);
        using var instructionBackground = new SolidBrush(Color.FromArgb(220, 18, 20, 28));
        using var instructionFont = new Font("Segoe UI", 16, FontStyle.Bold);
        eventArgs.Graphics.FillRectangle(instructionBackground, instructionRectangle);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            "Draw a box around “Results”.\nRelease to save. Esc to cancel.",
            instructionFont,
            instructionRectangle,
            Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _selectionStart = eventArgs.Location;
        _selection = Rectangle.Empty;
        _isSelecting = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        if (!_isSelecting)
        {
            return;
        }

        _selection = NormalizeRectangle(_selectionStart, eventArgs.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        if (!_isSelecting || eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _isSelecting = false;
        _selection = NormalizeRectangle(_selectionStart, eventArgs.Location);
        if (_selection.Width < 16 || _selection.Height < 12)
        {
            MessageBox.Show(
                "Draw a slightly larger box.",
                "Recognition area too small",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _selection = Rectangle.Empty;
            Invalidate();
            return;
        }

        SelectedArea = ClientToImageRectangle(_selection);
        SelectedImage = _screenshot.Clone(SelectedArea, PixelFormat.Format24bppRgb);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenshot.Dispose();
        }

        base.Dispose(disposing);
    }

    private Rectangle ClientToImageRectangle(Rectangle clientRectangle)
    {
        var scaleX = (double)_screenshot.Width / ClientSize.Width;
        var scaleY = (double)_screenshot.Height / ClientSize.Height;
        return new Rectangle(
            (int)Math.Round(clientRectangle.X * scaleX),
            (int)Math.Round(clientRectangle.Y * scaleY),
            Math.Max(1, (int)Math.Round(clientRectangle.Width * scaleX)),
            Math.Max(1, (int)Math.Round(clientRectangle.Height * scaleY)));
    }

    private static Rectangle NormalizeRectangle(Point first, Point second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X, second.X);
        var bottom = Math.Max(first.Y, second.Y);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
