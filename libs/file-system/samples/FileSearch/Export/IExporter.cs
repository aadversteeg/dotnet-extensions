using FileSearch.Handlers;

namespace FileSearch.Export;

public interface IExporter
{
    string Export(ISearchHandler handler);
}