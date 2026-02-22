using System;
using System.Collections.Generic;
using Ave.Extensions.ErrorPaths;
using Ave.Extensions.Functional;

namespace Ave.Extensions.FileSystem
{
    /// <summary>
    /// Base class for path mappers that provides shared relative path resolution logic.
    /// Platform-specific mappers should inherit from this class and implement the abstract methods.
    /// </summary>
    public abstract class PathMapperBase : IPathMapper
    {
        /// <inheritdoc />
        public abstract Result<string, Error> MapToSystemPath(CanonicalPath canonicalPath);

        /// <inheritdoc />
        public abstract Result<CanonicalPath, Error> MapToCanonicalPath(string systemPath);

        /// <inheritdoc />
        public Result<CanonicalPath, Error> MapToCanonicalPath(string relativePath, CanonicalPath basePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return Result<CanonicalPath, Error>.Failure(
                    FileSystemErrors.CanonicalPath.RelativePathNullOrEmpty);

            var relativeSegments = relativePath.Split(
                new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

            var resolvedSegments = new List<string>(basePath.Segments);

            foreach (var segment in relativeSegments)
            {
                if (segment == ".")
                    continue;

                if (segment == "..")
                {
                    if (resolvedSegments.Count == 0)
                        return Result<CanonicalPath, Error>.Failure(
                            FileSystemErrors.CanonicalPath.RelativePathEscapesRoot(relativePath));
                    resolvedSegments.RemoveAt(resolvedSegments.Count - 1);
                }
                else
                {
                    resolvedSegments.Add(segment);
                }
            }

            return CanonicalPath.Create(resolvedSegments.ToArray());
        }
    }
}
