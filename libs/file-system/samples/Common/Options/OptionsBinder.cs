using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;

namespace FileSystemSamples.Common.Options;

public static class OptionsBinder
{
    public static T ParseOptions<T>(InvocationContext context) where T : class, new()
    {
        var options = new T();
        var parseResult = context.ParseResult;
        
        foreach (var property in typeof(T).GetProperties())
        {
            Option? option = null;
            foreach (var opt in parseResult.RootCommandResult.Command.Options)
            {
                if (opt.Name.Replace("-", "") == property.Name.ToLower())
                {
                    option = opt;
                    break;
                }
            }
                
            if (option != null)
            {
                var value = parseResult.GetValueForOption(option);
                if (value != null)
                {
                    property.SetValue(options, value);
                }
            }
        }
        
        return options;
    }
}