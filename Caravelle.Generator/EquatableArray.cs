using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Caravelle.Generator;

// Grabbed from https://github.com/andrewlock/NetEscapades.EnumGenerators

/// <summary>
/// An immutable, equatable array. This is equivalent to <see cref="ImmutableArray{T}"/> but with value equality support.
/// </summary>
/// <typeparam name="T">The type of values in the array.</typeparam>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyCollection<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// The underlying <typeparamref name="T"/> array.
    /// </summary>
    private readonly T[]? _array;

    /// <summary>
    /// Creates a new <see cref="EquatableArray{T}"/> instance.
    /// </summary>
    /// <param name="collection">The input <see cref="IEnumerable{T}"/> to wrap.</param>
    public EquatableArray(IEnumerable<T> collection)
    {
        _array = collection as T[] ?? collection.ToArray();
    }

    /// <sinheritdoc/>
    public bool Equals(EquatableArray<T> array)
    {
        return AsSpan().SequenceEqual(array.AsSpan());
    }

    /// <sinheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> array && this.Equals(array);
    }

    /// <sinheritdoc/>
    public override int GetHashCode()
    {
        if (_array is not { } array)
        {
            return 0;
        }

        HashCode hashCode = default;

        foreach (T item in array)
        {
            hashCode.Add(item);
        }

        return hashCode.ToHashCode();
    }

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> wrapping the current items.
    /// </summary>
    /// <returns>A <see cref="ReadOnlySpan{T}"/> wrapping the current items.</returns>
    public ReadOnlySpan<T> AsSpan()
    {
        return _array.AsSpan();
    }

    /// <sinheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return ((IEnumerable<T>)(_array ?? [])).GetEnumerator();
    }

    /// <sinheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<T>)(_array ?? [])).GetEnumerator();
    }

    public int Count => _array?.Length ?? 0;
    public static EquatableArray<T> Empty = new([]);

    /// <summary>
    /// Checks whether two <see cref="EquatableArray{T}"/> values are the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are equal.</returns>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Checks whether two <see cref="EquatableArray{T}"/> values are not the same.
    /// </summary>
    /// <param name="left">The first <see cref="EquatableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are not equal.</returns>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
    {
        return !left.Equals(right);
    }

    public T this[int i] => _array is not null ? _array[i] : throw new ArgumentOutOfRangeException();
}