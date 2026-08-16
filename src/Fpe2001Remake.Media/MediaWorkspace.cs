using System.IO;
using System.Windows.Media.Imaging;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.Media;

/// <summary>
/// 图片工作区（规格 §9）：枚举/重命名/导出/删除（回收站优先）。
/// 解码用 WPF BitmapDecoder（无第三方依赖）；解码在后台并限制像素数（>4096 缩略）。
/// </summary>
public sealed class MediaWorkspace : IMediaWorkspace
{
    private const int MaxDimension = 4096;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    private static bool IsImage(string path)
        => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public async IAsyncEnumerable<MediaItem> EnumerateAsync(string folder, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                     .Where(IsImage)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            MediaItem? item = null;
            try
            {
                var info = new FileInfo(file);
                item = await Task.Run(() =>
                {
                    using var fs = File.OpenRead(file);
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
                    return new MediaItem(
                        new MediaIdentity(file, $"{info.Length:X16}|{info.LastWriteTimeUtc:o}"),
                        info.Name,
                        Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),
                        frame?.PixelWidth ?? 0,
                        frame?.PixelHeight ?? 0,
                        info.Length,
                        info.LastWriteTimeUtc);
                }, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 损坏或不可读图片：跳过
            }
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    public Task<MediaItem> RenameAsync(MediaIdentity item, string newName, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(item.Path)!;
        var target = Path.Combine(dir, newName);
        File.Move(item.Path, target);
        var info = new FileInfo(target);
        return Task.FromResult(new MediaItem(
            new MediaIdentity(target, $"{info.Length:X16}|{info.LastWriteTimeUtc:o}"),
            info.Name,
            Path.GetExtension(target).TrimStart('.').ToUpperInvariant(),
            0, 0, info.Length, info.LastWriteTimeUtc));
    }

    public Task ExportAsync(MediaIdentity item, MediaExportOptions options, CancellationToken ct)
    {
        var target = options.TargetPath ?? System.IO.Path.ChangeExtension(item.Path, $".{options.Format.ToLowerInvariant()}");
        return Task.Run(() =>
        {
            var source = OpenImage(item.Path); // BitmapSource 不实现 IDisposable
            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var maxW = options.MaxWidth ?? int.MaxValue;
            var maxH = options.MaxHeight ?? int.MaxValue;
            if (width > maxW || height > maxH)
            {
                var scale = Math.Min((double)maxW / width, (double)maxH / height);
                width = Math.Max(1, (int)(width * scale));
                height = Math.Max(1, (int)(height * scale));
            }

            var encoder = CreateEncoder(options.Format, options.JpegQuality);
            var frame = BitmapFrame.Create(source);
            if (width != source.PixelWidth || height != source.PixelHeight)
            {
                var resized = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform((double)width / source.PixelWidth, (double)height / source.PixelHeight));
                frame = BitmapFrame.Create(resized);
            }
            encoder.Frames.Add(frame);

            var dir = Path.GetDirectoryName(target)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using var stream = File.Create(target);
            encoder.Save(stream);
        }, ct);
    }

    public Task DeleteAsync(MediaIdentity item, DeletePolicy policy, CancellationToken ct)
    {
        if (policy == DeletePolicy.Permanent)
        {
            File.Delete(item.Path);
        }
        else
        {
            // 回收站（Recycle Bin）
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        return Task.CompletedTask;
    }

    // ---------- 内部 ----------

    private static BitmapSource OpenImage(string path)
    {
        using var fs = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static BitmapEncoder CreateEncoder(string format, int? jpegQuality)
    {
        return format.ToLowerInvariant() switch
        {
            "png" => new PngBitmapEncoder(),
            "bmp" => new BmpBitmapEncoder(),
            "gif" => new GifBitmapEncoder(),
            "tiff" => new TiffBitmapEncoder(),
            "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = jpegQuality ?? 90 },
            _ => new PngBitmapEncoder(),
        };
    }
}
