using System.Text;

namespace Fpe2001Remake.BinaryEditor.Core;

/// <summary>文本编码（规格 §5.3：ASCII/UTF-8/UTF-16LE/GBK）。</summary>
public static class TextCodec
{
    static TextCodec()
    {
        // GBK 等代码页需要 CodePagesEncodingProvider（.NET Core 默认不注册）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<string> Supported { get; } = ["ASCII", "UTF-8", "UTF-16LE", "GBK"];

    public static Encoding GetEncoding(string name) => name switch
    {
        "ASCII" => Encoding.ASCII,
        "UTF-8" => Encoding.UTF8,
        "UTF-16LE" => Encoding.Unicode,
        "GBK" => Encoding.GetEncoding("GBK"),
        _ => Encoding.ASCII,
    };

    /// <summary>安全解码（无效字节替换为 U+FFFD，不抛异常）。</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, string encoding)
    {
        var decoder = GetEncoding(encoding).GetDecoder();
        var charCount = decoder.GetCharCount(bytes, flush: true);
        var chars = new char[charCount];
        decoder.GetChars(bytes, chars, flush: true);
        return new string(chars);
    }

    public static byte[] Encode(string text, string encoding)
        => GetEncoding(encoding).GetBytes(text);
}
