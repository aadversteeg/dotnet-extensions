namespace FileSystemSamples.Common.Export.Formatters;

public abstract class CsvFormatter<T> : BaseFormatter<T>
{
    public override string FileExtension => ".csv";
    public override string ContentType => "text/csv";
    
    protected static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;
            
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        
        return field;
    }
}