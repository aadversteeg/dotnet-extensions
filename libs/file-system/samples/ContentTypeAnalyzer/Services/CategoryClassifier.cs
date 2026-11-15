using ContentTypeAnalyzer.Models;

namespace ContentTypeAnalyzer.Services
{
    public class CategoryClassifier
    {
        public static FileCategory DetermineCategory(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return FileCategory.Unknown;

            return contentType.ToLowerInvariant() switch
            {
                var ct when ct.StartsWith("image/") => FileCategory.Image,
                var ct when ct.StartsWith("audio/") => FileCategory.Audio,
                var ct when ct.StartsWith("video/") => FileCategory.Video,
                var ct when ct.StartsWith("text/") => FileCategory.Text,
                
                "application/pdf" => FileCategory.Document,
                "application/msword" => FileCategory.Document,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => FileCategory.Document,
                "application/vnd.ms-excel" => FileCategory.Document,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => FileCategory.Document,
                "application/vnd.ms-powerpoint" => FileCategory.Document,
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => FileCategory.Document,
                "application/rtf" => FileCategory.Document,
                
                "application/zip" => FileCategory.Archive,
                "application/x-rar-compressed" => FileCategory.Archive,
                "application/x-7z-compressed" => FileCategory.Archive,
                "application/gzip" => FileCategory.Archive,
                "application/x-tar" => FileCategory.Archive,
                "application/x-bzip2" => FileCategory.Archive,
                
                var ct when ct.StartsWith("application/x-") && (ct.Contains("executable") || ct.Contains(".exe")) => FileCategory.Application,
                "application/x-msdownload" => FileCategory.Application,
                "application/x-dosexec" => FileCategory.Application,
                "application/octet-stream" => FileCategory.Application,
                
                "application/json" => FileCategory.Data,
                "application/xml" => FileCategory.Data,
                "application/x-sqlite3" => FileCategory.Data,
                "application/x-database" => FileCategory.Data,
                
                _ => FileCategory.Unknown
            };
        }

        public static string GetContentSubType(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return "Unknown";

            var parts = contentType.Split('/');
            if (parts.Length != 2)
                return "Unknown";

            return parts[1] switch
            {
                "jpeg" => "JPEG",
                "jpg" => "JPEG", 
                "png" => "PNG",
                "gif" => "GIF",
                "bmp" => "Bitmap",
                "tiff" => "TIFF",
                "webp" => "WebP",
                
                "mpeg" => "MPEG",
                "mp3" => "MP3",
                "wav" => "WAV",
                "flac" => "FLAC",
                "ogg" => "OGG",
                
                "mp4" => "MP4",
                "avi" => "AVI",
                "mkv" => "Matroska",
                "mov" => "QuickTime",
                "wmv" => "WMV",
                
                "pdf" => "PDF",
                "msword" => "Word",
                "rtf" => "RTF",
                
                "zip" => "ZIP",
                "x-rar-compressed" => "RAR",
                "x-7z-compressed" => "7-Zip",
                
                "plain" => "Plain Text",
                "html" => "HTML",
                "css" => "CSS",
                "javascript" => "JavaScript",
                
                _ => parts[1].ToUpperInvariant()
            };
        }

        public static string[] GetExpectedExtensions(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return System.Array.Empty<string>();

            return contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => new[] { ".jpg", ".jpeg" },
                "image/png" => new[] { ".png" },
                "image/gif" => new[] { ".gif" },
                "image/bmp" => new[] { ".bmp" },
                "image/tiff" => new[] { ".tiff", ".tif" },
                "image/webp" => new[] { ".webp" },
                
                "audio/mpeg" => new[] { ".mp3" },
                "audio/wav" => new[] { ".wav" },
                "audio/flac" => new[] { ".flac" },
                "audio/ogg" => new[] { ".ogg" },
                "audio/mp4" => new[] { ".m4a", ".aac" },
                
                "video/mp4" => new[] { ".mp4" },
                "video/avi" => new[] { ".avi" },
                "video/x-matroska" => new[] { ".mkv" },
                "video/quicktime" => new[] { ".mov" },
                "video/x-ms-wmv" => new[] { ".wmv" },
                
                "application/pdf" => new[] { ".pdf" },
                "application/msword" => new[] { ".doc" },
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => new[] { ".docx" },
                "application/vnd.ms-excel" => new[] { ".xls" },
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => new[] { ".xlsx" },
                "application/vnd.ms-powerpoint" => new[] { ".ppt" },
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => new[] { ".pptx" },
                "application/rtf" => new[] { ".rtf" },
                
                "application/zip" => new[] { ".zip" },
                "application/x-rar-compressed" => new[] { ".rar" },
                "application/x-7z-compressed" => new[] { ".7z" },
                "application/gzip" => new[] { ".gz" },
                "application/x-tar" => new[] { ".tar" },
                
                "application/x-msdownload" => new[] { ".exe", ".dll", ".com" },
                "application/x-dosexec" => new[] { ".exe" },
                
                "text/plain" => new[] { ".txt" },
                "text/html" => new[] { ".html", ".htm" },
                "text/css" => new[] { ".css" },
                "text/javascript" => new[] { ".js" },
                "application/json" => new[] { ".json" },
                "application/xml" => new[] { ".xml" },
                
                _ => System.Array.Empty<string>()
            };
        }
    }
}