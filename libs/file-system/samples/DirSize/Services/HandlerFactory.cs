using System;
using DirSize.Handlers;
using DirSize.Models;

namespace DirSize.Services;

public class HandlerFactory
{
    public ISizeHandler CreateHandler(HandlerType handlerType)
    {
        return handlerType switch
        {
            HandlerType.Simple => new SimpleSizeHandler(),
            HandlerType.Detailed => new DetailedSizeHandler(),
            HandlerType.Tree => new TreeSizeHandler(),
            _ => throw new ArgumentOutOfRangeException(nameof(handlerType), handlerType, "Unsupported handler type")
        };
    }
}