using System.Text;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.Application;

/// <summary>
/// 旧配置导入预览（规格 M08 / P3）：
/// 识别文本（INI/CSV/TSV/键值）与二进制格式，提取字段供设置页展示与导入。
/// 只读预览，不写入任何目标。
/// </summary>
public sealed class LegacyImportService : ILegacyImportService
{
    private const int MaxPreviewBytes = 64 * 1024;

    public async ValueTask<LegacyImportPreview> PreviewAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("文件不存在。", path);
        }

        var info = new FileInfo(path);
        var bytes = new byte[Math.Min(info.Length, MaxPreviewBytes)];
        await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var read = await fs.ReadAsync(bytes, ct);
            Array.Resize(ref bytes, read);
        }

        var fields = new List<LegacyImportField>();
        var warnings = new List<string>();

        var format = DetectFormat(path, bytes);
        switch (format)
        {
            case "INI":
                ParseIni(bytes, fields, warnings);
                break;
            case "CSV":
                ParseDelimited(bytes, ',', fields, warnings);
                break;
            case "TSV":
                ParseDelimited(bytes, '\t', fields, warnings);
                break;
            default:
                ParseBinary(info, bytes, fields, warnings);
                break;
        }

        return new LegacyImportPreview(path, format, fields, warnings);
    }

    // ---------- 检测 ----------

    private static string DetectFormat(string path, byte[] bytes)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".ini" or ".cfg" or ".conf")
        {
            return "INI";
        }
        if (ext is ".csv")
        {
            return "CSV";
        }
        if (ext is ".tsv" or ".tab" or ".fp")
        {
            return "TSV";
        }

        // 内容启发：前 4KB 无 NUL → 文本
        var sample = bytes.AsSpan(0, Math.Min(bytes.Length, 4096));
        if (sample.IndexOf((byte)0) < 0)
        {
            var text = Encoding.UTF8.GetString(sample);
            if (text.Contains('='))
            {
                return "INI";
            }
            if (text.Contains('\t'))
            {
                return "TSV";
            }
            if (text.Contains(','))
            {
                return "CSV";
            }
            return "Text";
        }
        return "Binary";
    }

    // ---------- 解析 ----------

    private static void ParseIni(byte[] bytes, List<LegacyImportField> fields, List<string> warnings)
    {
        var text = SafeDecode(bytes);
        var lineCount = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                fields.Add(new LegacyImportField("节", line, IsUnknown: false));
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                var name = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                fields.Add(new LegacyImportField(name, value, IsUnknown: false));
            }
            else
            {
                fields.Add(new LegacyImportField($"行{lineCount}", line, IsUnknown: true));
                warnings.Add($"无法识别行 {lineCount}：{Truncate(line, 40)}");
            }
            lineCount++;
            if (lineCount > 200)
            {
                warnings.Add("预览截断：超过 200 行。");
                break;
            }
        }
    }

    private static void ParseDelimited(byte[] bytes, char separator, List<LegacyImportField> fields, List<string> warnings)
    {
        var text = SafeDecode(bytes);
        var lineCount = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }
            var parts = line.Split(separator);
            if (parts.Length >= 2)
            {
                fields.Add(new LegacyImportField(parts[0].Trim(), string.Join(separator.ToString(), parts[1..]).Trim(), IsUnknown: false));
            }
            else
            {
                fields.Add(new LegacyImportField($"行{lineCount}", line, IsUnknown: true));
                warnings.Add($"无法识别行 {lineCount}：{Truncate(line, 40)}");
            }
            lineCount++;
            if (lineCount > 200)
            {
                warnings.Add("预览截断：超过 200 行。");
                break;
            }
        }
    }

    private static void ParseBinary(FileInfo info, byte[] bytes, List<LegacyImportField> fields, List<string> warnings)
    {
        fields.Add(new LegacyImportField("文件大小", $"{info.Length} 字节 (0x{info.Length:X})", IsUnknown: false));
        fields.Add(new LegacyImportField("修改时间", info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), IsUnknown: false));

        if (bytes.Length >= 4)
        {
            var magic = Encoding.ASCII.GetString(bytes, 0, Math.Min(4, bytes.Length));
            fields.Add(new LegacyImportField("魔数(ASCII)", $"0x{Convert.ToHexString(bytes.AsSpan(0, Math.Min(4, bytes.Length)))} '{Sanitize(magic)}'", IsUnknown: bytes.Length < 4));
        }
        if (bytes.Length >= 16)
        {
            var header = Convert.ToHexString(bytes.AsSpan(0, 16));
            fields.Add(new LegacyImportField("头部 16 字节", header, IsUnknown: false));
        }
        if (info.Length > MaxPreviewBytes)
        {
            warnings.Add($"文件超过预览上限（{MaxPreviewBytes / 1024} KiB），仅读取头部。");
        }
    }

    // ---------- 辅助 ----------

    private static string SafeDecode(byte[] bytes)
    {
        // 优先 UTF-8（含 BOM 检测）；GBK 兜底
        try
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return Encoding.GetEncoding("GBK").GetString(bytes);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string Sanitize(string s) => new(s.Select(c => c is >= ' ' and <= '~' ? c : '.').ToArray());
}
