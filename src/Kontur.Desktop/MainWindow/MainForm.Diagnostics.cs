using Kontur.Core;

namespace Kontur.Desktop;

internal sealed partial class MainForm
{
    internal int MainNumericInputCount => inputs.Count;
    internal string StatusText => status.Text;
    internal bool DrawingLabelsInside => sketch.LabelsInside;
    internal bool DrawingLabelsOverlap => sketch.LabelsOverlap;
    internal bool RearWallsClosed => sketch.RearWallsClosed;
    internal bool DrawingLabelsClearOfMaterial => sketch.LabelsClearOfMaterial;
    internal bool DrawingBoreDimensionsClearOfMaterial => sketch.BoreDimensionsClearOfMaterial;
    internal int DrawingDpi => sketch.DeviceDpi;
    internal int DrawingDimensionCount => sketch.DimensionCount;
    internal float DrawingScale => sketch.DrawingScale;
    internal float DrawingCornerRadiusPixels => sketch.CornerRadiusPixels;
    internal int DrawingCornerArcCount => sketch.CornerArcCount;
    internal bool DrawingLabelsClearOfRearEdges => sketch.LabelsClearOfRearEdges;

    internal string DimensionText(string key) => sketch.DimensionText(key);
    internal Parameters? ReadForSmoke() => ReadParameters(false);
    internal Parameters? ReadDraftForSmoke() => ReadParameters(false, false);
    internal Control DrawingControl => sketch;

    internal RectangleF DimensionHitBounds(string key) => sketch.DimensionHitBounds(key);
    internal string? HitDimension(Point point) => sketch.HitDimension(point);
    internal void CaptureForm(string path)
    {
        PerformLayout();
        Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        // DrawToBitmap reverses overlapping child windows. Composite the actual topmost flyout last.
        if (dimensionFlyout.Visible)
        {
            using var flyoutImage = new Bitmap(dimensionFlyout.Width, dimensionFlyout.Height);
            dimensionFlyout.DrawToBitmap(flyoutImage, new Rectangle(Point.Empty, dimensionFlyout.Size));
            using var graphics = Graphics.FromImage(bitmap);
            var at = PointToScreen(dimensionFlyout.Location);
            graphics.DrawImageUnscaled(flyoutImage, at.X - Left, at.Y - Top);
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    internal void CaptureProcessingView(string path)
    {
        OpenProcessing();
        CaptureForm(path);
        CloseProcessing();
    }

    internal void CaptureExportDialog(IReadOnlyList<string> files, string path)
    {
        using var dialog = new ExportResultDialog(files);
        CaptureOwnedDialog(dialog, path);
    }

    private void CaptureOwnedDialog(Form dialog, string path)
    {
        dialog.Show(this);
        Application.DoEvents();
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        dialog.Close();
    }

    // Text/layout stress simulation only; this does not change Windows display settings.
    internal void SimulateDpiText(int targetDpi)
    {
        var factor = targetDpi / (float)DeviceDpi;
        var controls = new List<Control>();
        void Collect(Control parent)
        {
            controls.Add(parent);
            foreach (Control child in parent.Controls)
            {
                Collect(child);
            }
        }

        Collect(this);
        var originals = controls.Select(c => (Control: c, Font: c.Font)).ToList();
        var originalSize = ClientSize;
        Scale(new SizeF(factor, factor));
        ClientSize = new Size((int)Math.Round(originalSize.Width * factor), (int)Math.Round(originalSize.Height * factor));
        foreach (var item in originals)
        {
            var font = new Font(item.Font.FontFamily, item.Font.Size * factor, item.Font.Style);
            smokeFonts.Add(font);
            item.Control.Font = font;
        }

        sketch.SimulatedTextScale = factor;
        PerformLayout();
        Refresh();
    }
}
