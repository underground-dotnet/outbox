namespace Underground.Outbox.SourceGenerator;

/// <summary>
/// A list with value-based equality for proper incremental generator caching.
/// Based on Microsoft's recommended implementation.
/// https://github.com/dotnet/roslyn/blob/NET-SDK-10.0.101/docs/features/incremental-generators.cookbook.md#auto-interface-implementation
/// Alternatives:
/// - https://github.com/andrewlock/StronglyTypedId/blob/master/src/StronglyTypedIds/EquatableArray.cs
/// - https://github.com/CommunityToolkit/dotnet/blob/main/src/CommunityToolkit.Mvvm.SourceGenerators/Helpers/EquatableArray%7BT%7D.cs
/// </summary>
/// <remarks>
/// ImmutableArray uses reference equality which breaks incremental caching.
/// This wrapper ensures the generator can properly detect unchanged results.
/// </remarks>
internal sealed class EquatableList<T> : List<T>, IEquatable<EquatableList<T>>
{
    public bool Equals(EquatableList<T>? other)
    {
        if (other is null || Count != other.Count)
        {
            return false;
        }

        for (int i = 0; i < Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(this[i], other[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as EquatableList<T>);
    }

    public override int GetHashCode()
    {
        if (Count == 0)
        {
            return 0;
        }

        return this.Select(item => item?.GetHashCode() ?? 0).Aggregate((x, y) => x ^ y);
    }

    public static bool operator ==(EquatableList<T>? list1, EquatableList<T>? list2)
    {
        return ReferenceEquals(list1, list2)
            || (list1 is not null && list2 is not null && list1.Equals(list2));
    }

    public static bool operator !=(EquatableList<T>? list1, EquatableList<T>? list2)
    {
        return !(list1 == list2);
    }
}
