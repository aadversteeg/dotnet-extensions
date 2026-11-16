using System.Reflection;
using Ave.Documents.Pdf;
using Ave.Documents.Render.Pdf;

namespace UnitTests.Ave.Documents.Render.Pdf;

/// <summary>
/// The public API of the PDF packages answers every argument value with a value — an object, or an error value from the
/// operation that cannot go on — never with an exception: every public constructor, method and property of
/// Ave.Documents.Pdf and Ave.Documents.Render.Pdf is called with null for every reference and the default for every
/// value type (an unset ImmutableArray included), and every member of each object so made is called the same way.
/// </summary>
public class TotalApiTests
{
    private static readonly Assembly[] Packages = [typeof(PdfWriter).Assembly, typeof(PdfRenderer).Assembly];

    private static object? Default(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    private static object?[] Defaults(MethodBase method) => [.. method.GetParameters().Select(parameter => Default(parameter.ParameterType))];

    private static bool IsCallable(MethodBase method) =>
        !method.IsAbstract && !method.ContainsGenericParameters && method.GetParameters().All(parameter => !parameter.ParameterType.IsByRef && !parameter.ParameterType.IsPointer);

    /// <summary>Calls the member; the description of the exception it threw, or null.</summary>
    private static string? Thrown(MethodBase method, object? target)
    {
        try
        {
            var result = method is ConstructorInfo constructor ? constructor.Invoke(Defaults(method)) : method.Invoke(target, Defaults(method));
            if (result is System.Collections.IEnumerable sequence and not string && !result.GetType().IsValueType)
            {
                // A lazy sequence throws only when it is read. (An unset ImmutableArray, which a property may hold, is a
                // value, not a sequence still to be computed.)
                foreach (var _ in sequence)
                {
                }
            }

            return null;
        }
        catch (TargetInvocationException exception)
        {
            return $"{method.DeclaringType!.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))}): {exception.InnerException!.GetType().Name} {exception.InnerException.Message}";
        }
        catch (Exception exception) when (exception is not TargetInvocationException)
        {
            return $"{method.DeclaringType!.Name}.{method.Name}: {exception.GetType().Name} {exception.Message}";
        }
    }

    [Fact]
    public void No_public_member_throws_for_null_or_default_arguments()
    {
        var failures = new List<string>();
        foreach (var type in Packages.SelectMany(assembly => assembly.GetExportedTypes()).Where(type => !type.ContainsGenericParameters && !typeof(Delegate).IsAssignableFrom(type)))
        {
            var instances = new List<object>();
            foreach (var constructor in type.GetConstructors().Where(IsCallable))
            {
                if (Thrown(constructor, null) is { } failure)
                {
                    failures.Add(failure);
                }
                else
                {
                    instances.Add(constructor.Invoke(Defaults(constructor)));
                }
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(IsCallable);
            foreach (var method in methods)
            {
                var targets = method.IsStatic ? [null] : instances.Cast<object?>().ToList();
                failures.AddRange(targets.Select(target => Thrown(method, target)).OfType<string>());
            }
        }

        failures.Distinct().Should().BeEmpty("every member answers with a value: " + string.Join(" | ", failures.Distinct()));
    }
}
