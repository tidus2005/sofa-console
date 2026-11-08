using System.Text.Json;

namespace MouseSideLab;

// Preserve the original field-based settings while sharing the host's .NET 8 runtime.
internal sealed class JsonCodec
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = false };
    public T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
    public string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}
