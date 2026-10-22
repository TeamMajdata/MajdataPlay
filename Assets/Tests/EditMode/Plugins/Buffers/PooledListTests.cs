using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Buffers;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers construction, mutation, growth, copying, enumeration and disposal of <see cref="PooledList{T}"/>.</summary>
    public class PooledListTests
    {
        /// <summary>Creates a list backed by a recording pool and filled with the given items.</summary>
        /// <param name="pool">The pool that backs the list.</param>
        /// <param name="capacity">The initial capacity requested from the pool.</param>
        /// <param name="items">The items to add.</param>
        /// <returns>The new list.</returns>
        private static PooledList<int> CreateList(RecordingArrayPool<int> pool, int capacity, params int[] items)
        {
            var list = new PooledList<int>(capacity, pool);
            list.AddRange(items.AsSpan());
            return list;
        }

        /// <summary>Checks that the default constructor starts empty with the shared pool's minimum capacity.</summary>
        [Test]
        public void DefaultConstructor_StartsEmpty()
        {
            using var list = new PooledList<int>();

            Assert.That(list.Count, Is.EqualTo(0));
            Assert.That(list.Capacity, Is.GreaterThanOrEqualTo(8));
            Assert.That(((ICollection<int>)list).IsReadOnly, Is.False);
        }

        /// <summary>Checks that the capacity constructor rents at least the requested capacity.</summary>
        [Test]
        public void CapacityConstructor_RentsRequestedCapacity()
        {
            using var list = new PooledList<int>(20);

            Assert.That(list.Capacity, Is.GreaterThanOrEqualTo(20));
            Assert.That(list.Count, Is.EqualTo(0));
        }

        /// <summary>Checks that the collection constructor copies the items and rejects null.</summary>
        [Test]
        public void CollectionConstructor_CopiesItemsAndRejectsNull()
        {
            using var list = new PooledList<int>(Enumerable.Range(1, 12));

            Assert.That(list.ToArray(), Is.EqualTo(Enumerable.Range(1, 12).ToArray()));
            var exception = Assert.Throws<ArgumentNullException>(() => new PooledList<int>((IEnumerable<int>)null!));
            Assert.That(exception!.ParamName, Is.EqualTo("items"));
        }

        /// <summary>Checks that the pool constructor rents the initial array from that pool.</summary>
        [Test]
        public void PoolConstructor_RentsFromPool()
        {
            var pool = new RecordingArrayPool<int>();
            using var list = new PooledList<int>(4, pool);

            Assert.That(pool.RentedLengths, Is.EqualTo(new[] { 4 }));
            Assert.That(list.Capacity, Is.EqualTo(4));
        }

        /// <summary>Checks that adding beyond the capacity doubles it and keeps every item.</summary>
        [Test]
        public void Add_BeyondCapacity_DoublesCapacity()
        {
            var pool = new RecordingArrayPool<int>();
            using var list = CreateList(pool, 4, 1, 2, 3, 4);

            list.Add(5);

            Assert.That(list.Count, Is.EqualTo(5));
            Assert.That(list.Capacity, Is.EqualTo(8));
            Assert.That(list.ToArray(), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(pool.RentedLengths, Is.EqualTo(new[] { 4, 8 }));
            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            Assert.That(pool.ReturnedClearFlags, Is.EqualTo(new[] { true }));
        }

        /// <summary>Checks that a list created with zero capacity grows straight to sixteen elements.</summary>
        [Test]
        public void Add_FromZeroCapacity_GrowsToSixteen()
        {
            var pool = new RecordingArrayPool<int>();
            using var list = new PooledList<int>(0, pool);

            list.Add(1);

            Assert.That(list.Capacity, Is.EqualTo(16));
            Assert.That(list[0], Is.EqualTo(1));
            Assert.That(pool.ReturnedArrays, Is.Empty);
        }

        /// <summary>Checks that AddRange appends enumerable items in order, growing as needed, and rejects null.</summary>
        [Test]
        public void AddRange_Enumerable_AddsItemsInOrder()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 2);

            list.AddRange(new List<int> { 3, 1, 2 });

            Assert.That(list.ToArray(), Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.Throws<ArgumentNullException>(() => list.AddRange((IEnumerable<int>)null!));
        }

        /// <summary>Checks that the indexer reads and writes inside the count and rejects other indices.</summary>
        [Test]
        public void Indexer_ValidatesRange()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3);

            list[1] = 20;

            Assert.That(list[1], Is.EqualTo(20));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[-1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => list[3] = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => list[-1] = 0);
        }

        /// <summary>Checks that inserting before an element places the new item there and shifts the followers.</summary>
        [Test]
        public void Insert_PlacesItemAndShiftsFollowers()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3, 4);

            list.Insert(1, 9);

            Assert.That(list.Count, Is.EqualTo(5));
            Assert.That(list[0], Is.EqualTo(1));
            Assert.That(list[1], Is.EqualTo(9));
            Assert.That(list[2], Is.EqualTo(2));
            Assert.That(list[3], Is.EqualTo(3));
        }

        /// <summary>Checks that inserting outside the list is rejected.</summary>
        [Test]
        public void Insert_OutOfRange_Throws()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2);

            Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(-1, 0));
        }

        /// <summary>Checks that inserting keeps every existing element, including the last one.</summary>
        [Test]
        public void Insert_KeepsEveryExistingElement()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3);

            list.Insert(0, 9);
            list.Insert(3, 8);

            Assert.That(list.ToArray(), Is.EqualTo(new[] { 9, 1, 2, 8, 3 }));
        }

        /// <summary>Checks that inserting at the end of the list appends, as the IList contract allows.</summary>
        [Test]
        public void Insert_AtCount_Appends()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8);

            list.Insert(0, 1);
            list.Insert(1, 2);

            Assert.That(list.ToArray(), Is.EqualTo(new[] { 1, 2 }));
        }

        /// <summary>Checks that ReadUnsafe reads elements without range checks.</summary>
        [Test]
        public void ReadUnsafe_ReadsElements()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 4, 5, 6);

            Assert.That(list.ReadUnsafe(0), Is.EqualTo(4));
            Assert.That(list.ReadUnsafe(2), Is.EqualTo(6));
        }

        /// <summary>Checks that Clear empties the list and invalidates running enumerators.</summary>
        [Test]
        public void Clear_EmptiesListAndInvalidatesEnumerators()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3);
            var enumerator = list.GetEnumerator();

            list.Clear();

            Assert.That(list.Count, Is.EqualTo(0));
            Assert.That(list.Contains(1), Is.False);
            Assert.That(list.ToArray(), Is.Empty);
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }

        /// <summary>Checks that clearing an empty list does not invalidate enumerators.</summary>
        [Test]
        public void Clear_Empty_KeepsEnumeratorsValid()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8);
            var enumerator = list.GetEnumerator();

            list.Clear();

            Assert.That(enumerator.MoveNext(), Is.False);
        }

        /// <summary>Checks that Clear releases the references held by the backing array.</summary>
        [Test]
        public void Clear_ReleasesStoredReferences()
        {
            using var list = new PooledList<object>(4, new RecordingArrayPool<object>());
            list.Add(new object());

            list.Clear();

            var backing = PrivateMember.GetField<PooledArray<object>>(list, "_array");
            Assert.That(backing.AsArray()[0], Is.Null);
        }

        /// <summary>Checks that IndexOf finds stored items and reports missing ones.</summary>
        [Test]
        public void IndexOf_FindsStoredItems()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 5, 6, 7);

            Assert.That(list.IndexOf(6), Is.EqualTo(1));
            Assert.That(list.IndexOf(42), Is.EqualTo(-1));
        }

        /// <summary>Checks that IndexOf ignores the unused capacity beyond Count.</summary>
        [Test]
        public void IndexOf_IgnoresSpareCapacity()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 5, 6, 7);

            Assert.That(list.IndexOf(0), Is.EqualTo(-1));
        }

        /// <summary>Checks that Remove deletes a present item and reports a missing one.</summary>
        [Test]
        public void Remove_DeletesPresentItems()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 5, 6, 7);

            Assert.That(list.Remove(6), Is.True);
            Assert.That(list.Remove(42), Is.False);
            Assert.That(list.ToArray(), Is.EqualTo(new[] { 5, 7 }));
        }

        /// <summary>Checks that removing a value only present in the spare capacity reports false.</summary>
        [Test]
        public void Remove_ValueOnlyInSpareCapacity_ReturnsFalse()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 5, 6, 7);

            Assert.That(list.Remove(0), Is.False);
        }

        /// <summary>Checks that RemoveAt shifts following items, handles the last item and validates the index.</summary>
        [Test]
        public void RemoveAt_ShiftsAndValidates()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3, 4);

            list.RemoveAt(1);
            list.RemoveAt(2);

            Assert.That(list.ToArray(), Is.EqualTo(new[] { 1, 3 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(-1));
        }

        /// <summary>Checks that Contains only considers items inside the count.</summary>
        [Test]
        public void Contains_OnlyConsidersStoredItems()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3);

            Assert.That(list.Contains(3), Is.True);
            Assert.That(list.Contains(0), Is.False);
        }

        /// <summary>Checks copying into an array at an offset and the argument validation of that overload.</summary>
        [Test]
        public void CopyToArray_CopiesAndValidates()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3);
            var destination = new int[5];

            list.CopyTo(destination, 2);

            Assert.That(destination, Is.EqualTo(new[] { 0, 0, 1, 2, 3 }));
            Assert.Throws<ArgumentNullException>(() => list.CopyTo(null!, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.CopyTo(new int[5], -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.CopyTo(new int[5], 3));
        }

        /// <summary>Checks that copying an empty list leaves the destination untouched.</summary>
        [Test]
        public void CopyTo_EmptyList_CopiesNothing()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8);
            var destination = new[] { 7, 7 };
            Span<int> span = stackalloc int[] { 8, 8 };

            list.CopyTo(destination, 0);
            list.CopyTo(span);

            Assert.That(destination, Is.EqualTo(new[] { 7, 7 }));
            Assert.That(span.ToArray(), Is.EqualTo(new[] { 8, 8 }));
        }

        /// <summary>Checks copying into a span and rejecting a span that is too small.</summary>
        [Test]
        public void CopyToSpan_CopiesAndValidates()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3);
            var destination = new int[4];

            list.CopyTo(destination.AsSpan());

            Assert.That(destination, Is.EqualTo(new[] { 1, 2, 3, 0 }));
            Assert.Throws<ArgumentException>(() => list.CopyTo(new int[2].AsSpan()));
        }

        /// <summary>Checks that ToArray returns a shared empty array for an empty list and a copy otherwise.</summary>
        [Test]
        public void ToArray_CopiesItems()
        {
            using var empty = CreateList(new RecordingArrayPool<int>(), 8);
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2);

            Assert.That(empty.ToArray(), Is.SameAs(Array.Empty<int>()));
            var copy = list.ToArray();
            copy[0] = 99;
            Assert.That(list[0], Is.EqualTo(1));
        }

        /// <summary>Checks that the span, memory and segment views cover exactly the stored items.</summary>
        [Test]
        public void Views_CoverStoredItems()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2, 3);

            Assert.That(list.AsSpan().ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(list.AsMemory().ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
            var segment = list.AsArraySegment();
            Assert.That(segment.Offset, Is.EqualTo(0));
            Assert.That(segment.Count, Is.EqualTo(3));
            Assert.That(segment.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        /// <summary>Checks the capacity setter: shrinking below the count fails, the same value is a no-op and growing copies.</summary>
        [Test]
        public void Capacity_Setter_ValidatesAndCopies()
        {
            var pool = new RecordingArrayPool<int>();
            using var list = CreateList(pool, 4, 1, 2, 3);

            Assert.Throws<ArgumentOutOfRangeException>(() => list.Capacity = 2);
            list.Capacity = 4;
            Assert.That(pool.RentedLengths, Is.EqualTo(new[] { 4 }));

            list.Capacity = 10;

            Assert.That(list.Capacity, Is.EqualTo(10));
            Assert.That(list.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(pool.RentedLengths, Is.EqualTo(new[] { 4, 10 }));
        }

        /// <summary>Checks that growing an empty list's capacity does not copy anything.</summary>
        [Test]
        public void Capacity_SetterOnEmptyList_RentsNewArray()
        {
            var pool = new RecordingArrayPool<int>();
            using var list = CreateList(pool, 4);

            list.Capacity = 6;

            Assert.That(list.Capacity, Is.EqualTo(6));
            Assert.That(list.Count, Is.EqualTo(0));
            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
        }

        /// <summary>Checks that disposal returns the backing array once and that later use is rejected.</summary>
        [Test]
        public void Dispose_ReturnsArrayAndRejectsUse()
        {
            var pool = new RecordingArrayPool<int>();
            var list = CreateList(pool, 4, 1);

            list.Dispose();
            list.Dispose();

            Assert.That(pool.ReturnedArrays.Count, Is.EqualTo(1));
            Assert.That(pool.ReturnedClearFlags, Is.EqualTo(new[] { true }));
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => list.Add(2));
            Assert.Throws<ObjectDisposedException>(() => list.AddRange((IEnumerable<int>)new[] { 2 }));
            Assert.Throws<ObjectDisposedException>(() => list.AddRange(new[] { 2 }.AsSpan()));
            Assert.Throws<ObjectDisposedException>(() => list.Insert(0, 2));
            Assert.Throws<ObjectDisposedException>(() => _ = list.Capacity);
            Assert.Throws<ObjectDisposedException>(() => list.Capacity = 8);
            Assert.Throws<ObjectDisposedException>(() => _ = list[0]);
            Assert.Throws<ObjectDisposedException>(() => list[0] = 1);
            Assert.Throws<ObjectDisposedException>(() => list.Clear());
            Assert.Throws<ObjectDisposedException>(() => list.IndexOf(1));
            Assert.Throws<ObjectDisposedException>(() => list.Remove(1));
            Assert.Throws<ObjectDisposedException>(() => list.RemoveAt(0));
            Assert.Throws<ObjectDisposedException>(() => list.Contains(1));
            Assert.Throws<ObjectDisposedException>(() => list.CopyTo(new int[1], 0));
            Assert.Throws<ObjectDisposedException>(() => list.CopyTo(new int[1].AsSpan()));
            Assert.Throws<ObjectDisposedException>(() => list.ToArray());
            Assert.Throws<ObjectDisposedException>(() => list.AsSpan());
            Assert.Throws<ObjectDisposedException>(() => list.AsMemory());
        }

        /// <summary>Checks that foreach yields the stored items in order.</summary>
        [Test]
        public void Enumerator_YieldsItemsInOrder()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 3, 1, 2);
            var items = new List<int>();

            foreach (var item in list)
            {
                items.Add(item);
            }

            Assert.That(items, Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(((IEnumerable<int>)list).ToArray(), Is.EqualTo(new[] { 3, 1, 2 }));
        }

        /// <summary>Checks that the generic and non-generic enumerable interfaces enumerate the stored items.</summary>
        [Test]
        public void EnumerableInterfaces_YieldItems()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 4, 5);
            var generic = new List<int>();
            var nonGeneric = new List<object>();

            foreach (var item in (IEnumerable<int>)list)
            {
                generic.Add(item);
            }
            foreach (var item in (IEnumerable)list)
            {
                nonGeneric.Add(item);
            }

            Assert.That(generic, Is.EqualTo(new[] { 4, 5 }));
            Assert.That(nonGeneric, Is.EqualTo(new object[] { 4, 5 }));
        }

        /// <summary>Checks that modifying the list during enumeration is detected.</summary>
        [Test]
        public void Enumerator_DetectsModification()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2);
            var enumerator = list.GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);

            list[0] = 5;

            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }

        /// <summary>Checks the non-generic enumerator: Current before MoveNext fails, Reset restarts, and Reset after a change fails.</summary>
        [Test]
        public void Enumerator_NonGenericCurrentAndReset()
        {
            using var list = CreateList(new RecordingArrayPool<int>(), 8, 1, 2);
            IEnumerator enumerator = list.GetEnumerator();

            Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));
            Assert.That(enumerator.MoveNext(), Is.True);
            enumerator.Reset();
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(1));

            list.Add(3);

            Assert.Throws<InvalidOperationException>(() => enumerator.Reset());
            ((IDisposable)enumerator).Dispose();
        }

        /// <summary>Checks that the enumerator rejects use after the list has been disposed.</summary>
        [Test]
        public void Enumerator_AfterDispose_Throws()
        {
            var list = CreateList(new RecordingArrayPool<int>(), 8, 1);
            var enumerator = list.GetEnumerator();
            IEnumerator boxed = list.GetEnumerator();

            list.Dispose();

            Assert.Throws<ObjectDisposedException>(() => enumerator.MoveNext());
            Assert.Throws<ObjectDisposedException>(() => _ = enumerator.Current);
            Assert.Throws<ObjectDisposedException>(() => _ = boxed.Current);
            Assert.Throws<ObjectDisposedException>(() => boxed.Reset());
        }
    }
}
