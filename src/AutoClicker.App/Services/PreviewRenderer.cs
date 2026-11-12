using Avalonia;
using Avalonia.Media.Imaging;

namespace AutoClicker.App.Services;

internal static class PreviewRenderer
{
    public static void Save(Visual visual, string filePath)
    {
        var size = visual.Bounds.Size;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96));
        bitmap.Render(visual);
        bitmap.Save(filePath);
    }
}
