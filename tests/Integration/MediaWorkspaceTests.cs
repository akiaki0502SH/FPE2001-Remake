using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Media;
using Xunit;

namespace Fpe2001Remake.IntegrationTests;

/// <summary>P4 验收：图片工作区（规格 §9）。</summary>
public sealed class MediaWorkspaceTests : IDisposable
{
    private readonly string _dir;

    public MediaWorkspaceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        CreateTestPng(Path.Combine(_dir, "a.png"), 32, 32, Colors.Red);
        CreateTestPng(Path.Combine(_dir, "b.bmp"), 16, 16, Colors.Blue);
        File.WriteAllText(Path.Combine(_dir, "c.txt"), "not image");
    }

    [Fact]
    public async Task Enumerate_ReturnsOnlyImagesWithDimensions()
    {
        var workspace = new MediaWorkspace();
        var items = new List<MediaItem>();
        await foreach (var item in workspace.EnumerateAsync(_dir, CancellationToken.None))
        {
            items.Add(item);
        }

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Name == "a.png" && i.Width == 32 && i.Height == 32);
        Assert.Contains(items, i => i.Name == "b.bmp" && i.Format == "BMP");
    }

    [Fact]
    public async Task Export_PngToJpg_ProducesJpg()
    {
        var workspace = new MediaWorkspace();
        MediaItem? source = null;
        await foreach (var item in workspace.EnumerateAsync(_dir, CancellationToken.None))
        {
            if (item.Name == "a.png")
            {
                source = item;
                break;
            }
        }
        Assert.NotNull(source);

        var target = Path.Combine(_dir, "exported.jpg");
        await workspace.ExportAsync(source!.Identity, new MediaExportOptions("jpg", JpegQuality: 85, TargetPath: target), CancellationToken.None);
        Assert.True(File.Exists(target));
        Assert.True(new FileInfo(target).Length > 0);
    }

    [Fact]
    public async Task Rename_UpdatesNameAndKeepsFile()
    {
        var workspace = new MediaWorkspace();
        MediaItem? source = null;
        await foreach (var item in workspace.EnumerateAsync(_dir, CancellationToken.None))
        {
            if (item.Name == "a.png")
            {
                source = item;
                break;
            }
        }
        Assert.NotNull(source);

        var renamed = await workspace.RenameAsync(source!.Identity, "renamed.png", CancellationToken.None);
        Assert.Equal("renamed.png", renamed.Name);
        Assert.True(File.Exists(Path.Combine(_dir, "renamed.png")));
        Assert.False(File.Exists(source.Identity.Path));
    }

    [Fact]
    public async Task Delete_Permanent_RemovesFile()
    {
        var workspace = new MediaWorkspace();
        MediaItem? source = null;
        await foreach (var item in workspace.EnumerateAsync(_dir, CancellationToken.None))
        {
            if (item.Name == "b.bmp")
            {
                source = item;
                break;
            }
        }
        Assert.NotNull(source);

        await workspace.DeleteAsync(source!.Identity, DeletePolicy.Permanent, CancellationToken.None);
        Assert.False(File.Exists(source.Identity.Path));
    }

    private static void CreateTestPng(string path, int width, int height, Color color)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 0xFF;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);

        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() == ".png"
            ? new PngBitmapEncoder()
            : new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }
}
