using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fpe2001Remake.Domain;

/// <summary>
/// 逻辑地址：地址空间 + 域名 + 64 位值（规格 §3）。
/// 地址表跨会话只保存逻辑地址，不持久化宿主地址。
/// </summary>
[JsonConverter(typeof(LogicalAddressJsonConverter))]
public readonly record struct LogicalAddress(
    AddressSpace Space,
    string DomainId,
    ulong Value,
    Endianness Endianness = Endianness.LittleEndian,
    byte PointerWidthBits = 0)
{
    public override string ToString() => $"{Space}:{DomainId}:0x{Value:X16}";
}

/// <summary>
/// JSON 中 ulong 一律使用十六进制字符串（规格 §12）。
/// 输出形如 { "Space": 0, "DomainId": "", "Value": "0000000000001000", ... }。
/// </summary>
public sealed class LogicalAddressJsonConverter : JsonConverter<LogicalAddress>
{
    public override LogicalAddress Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("LogicalAddress 必须是 JSON 对象。");

        var space = AddressSpace.HostVirtual;
        var domainId = "";
        ulong value = 0;
        var endianness = Endianness.LittleEndian;
        byte pointerWidthBits = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("LogicalAddress 属性名无效。");

            var property = reader.GetString();
            reader.Read();

            switch (property)
            {
                case "Space":
                    space = (AddressSpace)reader.GetInt32();
                    break;
                case "DomainId":
                    domainId = reader.GetString() ?? "";
                    break;
                case "Value":
                    value = Convert.ToUInt64(reader.GetString(), 16);
                    break;
                case "Endianness":
                    endianness = (Endianness)reader.GetInt32();
                    break;
                case "PointerWidthBits":
                    pointerWidthBits = reader.GetByte();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new LogicalAddress(space, domainId, value, endianness, pointerWidthBits);
    }

    public override void Write(
        Utf8JsonWriter writer, LogicalAddress value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Space", (int)value.Space);
        writer.WriteString("DomainId", value.DomainId);
        writer.WriteString("Value", value.Value.ToString("X16"));
        writer.WriteNumber("Endianness", (int)value.Endianness);
        writer.WriteNumber("PointerWidthBits", value.PointerWidthBits);
        writer.WriteEndObject();
    }
}
