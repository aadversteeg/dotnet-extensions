using System.Collections.Generic;
using Ave.Extensions.Documents.Images;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.Documents.Fluent;

/// <summary>
/// The images of one document: each file (by its content, <see cref="ImageFile.Id"/>) decoded once however many
/// elements show it, and all of them together charged to one budget of decoded pixels
/// (<see cref="ImageLimits.MaxDocumentPixels"/>), from each file's header before any sample is made.
/// </summary>
internal sealed class ImageDecodes
{
    private readonly Dictionary<ImageId, Result<ImageData, Error>> _decoded = [];
    private readonly ImagePixelBudget _budget = new();

    /// <summary>The decoded image of a file, or why it does not decode.</summary>
    public Result<ImageData, Error> Decode(ImageFile file)
    {
        if (!_decoded.TryGetValue(file.Id, out var decoded))
        {
            decoded = ImageDecoder.Decode(file.Bytes.AsMemory(), _budget);
            _decoded[file.Id] = decoded;
        }

        return decoded;
    }
}
