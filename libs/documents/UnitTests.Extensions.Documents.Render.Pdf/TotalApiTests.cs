using System.Reflection;
using Ave.Extensions.Documents.Pdf;
using Ave.Extensions.Documents.Render.Pdf;

namespace UnitTests.Extensions.Documents.Render.Pdf;

/// <summary>
/// The public API of the PDF packages answers every argument value with a value — an object, or an error value from the
/// operation that cannot go on — never with an exception: every public constructor, method and property of
/// Ave.Extensions.Documents.Pdf and Ave.Extensions.Documents.Render.Pdf is called with null for every reference and the default for every
/// value type (an unset ImmutableArray included), and every member of each object so made is called the same way. A
/// parameter that takes a sequence is also given an unset ImmutableArray: boxed, it is not null, but it cannot be read.
/// A foreign case of an abstract type cannot reach a writer: no other assembly can derive from
/// <see cref="IndirectObject"/>, and a foreign <see cref="PdfObject"/> or XMP value is an error value when it is written
/// (Pdf PdfObjectRulesTests, ConformanceBuildingBlockTests).
/// </summary>
public class TotalApiTests
{
    private static readonly Assembly[] Packages = [typeof(PdfWriter).Assembly, typeof(PdfRenderer).Assembly];

    private static object? Default(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    private static object?[] Defaults(MethodBase method) => [.. method.GetParameters().Select(parameter => Default(parameter.ParameterType))];

    /// <summary>For a sequence parameter that an ImmutableArray can be passed to: the unset ImmutableArray, boxed; else
    /// the default.</summary>
    private static object? Unset(Type type)
    {
        var item = type.IsGenericType && type.GetGenericArguments().Length == 1 ? type.GetGenericArguments()[0] : null;
        var array = item is null ? null : typeof(System.Collections.Immutable.ImmutableArray<>).MakeGenericType(item);
        return array is not null && !type.IsValueType && type.IsAssignableFrom(array) ? Activator.CreateInstance(array) : Default(type);
    }

    private static object?[] Unsets(MethodBase method) => [.. method.GetParameters().Select(parameter => Unset(parameter.ParameterType))];

    private static bool IsCallable(MethodBase method) =>
        !method.IsAbstract && !method.ContainsGenericParameters && method.GetParameters().All(parameter => !parameter.ParameterType.IsByRef && !parameter.ParameterType.IsPointer);

    /// <summary>Calls the member with the given arguments (<see cref="Defaults"/> unless said otherwise); the description of
    /// the exception it threw, or null.</summary>
    private static string? Thrown(MethodBase method, object? target, object?[]? arguments = null)
    {
        arguments ??= Defaults(method);
        try
        {
            var result = method is ConstructorInfo constructor ? constructor.Invoke(arguments) : method.Invoke(target, arguments);
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

    [Fact]
    public void No_public_member_throws_for_an_unset_ImmutableArray_given_as_a_sequence()
    {
        var failures = Packages.SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.ContainsGenericParameters && !typeof(Delegate).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors()))
            .Where(IsCallable)
            .Where(method => method.GetParameters().Any(parameter => Unset(parameter.ParameterType) is not null && !parameter.ParameterType.IsValueType))
            .Select(method => Thrown(method, null, Unsets(method)))
            .OfType<string>()
            .Distinct()
            .ToList();

        failures.Should().BeEmpty("an unset ImmutableArray is read as an empty sequence: " + string.Join(" | ", failures));
    }

    [Fact]
    public void No_other_assembly_can_derive_from_IndirectObject()
    {
        // A record's copy constructor is always protected, so only an abstract member that no other assembly can
        // override keeps a derived type out: a foreign case could not implement it.
        typeof(IndirectObject).GetMembers(BindingFlags.Instance | BindingFlags.NonPublic)
            .OfType<PropertyInfo>()
            .Should().Contain(property => property.GetMethod!.IsAbstract && property.GetMethod.IsAssembly);
    }
}
