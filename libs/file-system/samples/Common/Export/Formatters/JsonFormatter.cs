using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileSystemSamples.Common.Export.Formatters;

public class JsonFormatter<T> : BaseFormatter<T>
{
    private readonly JsonSerializerOptions _options;
    
    public override string FileExtension => ".json";
    public override string ContentType => "application/json";
    
    public JsonFormatter()
    {
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }
    
    public override string Format(T data)
    {
        return JsonSerializer.Serialize(data, _options);
    }
}