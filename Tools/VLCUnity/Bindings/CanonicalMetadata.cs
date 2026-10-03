// Cecil sorts these tables only by their owner. Equal-owner rows can appear in
// different orders with the .NET Framework and .NET Array.Sort implementations.
// ECMA-335 II.22: neither table has incoming table-index references, so ordering
// equal-owner rows by their full bytes changes no metadata token or IL operand.
using System;
using System.Collections.Generic;

public static class VlcBindingCanonicalMetadata
{
    static ushort U16(byte[] bytes, int offset) { return BitConverter.ToUInt16(bytes, offset); }
    static uint U32(byte[] bytes, int offset) { return BitConverter.ToUInt32(bytes, offset); }
    static int Index(uint[] rows, int table) { return rows[table] < 65536 ? 2 : 4; }
    static int Coded(uint[] rows, int tags, params int[] tables)
    {
        uint maximum = 0;
        foreach (int table in tables) maximum = Math.Max(maximum, rows[table]);
        return maximum < (1u << (16 - tags)) ? 2 : 4;
    }
    static int FileOffset(byte[] bytes, uint rva, int sections, int count)
    {
        for (int index = 0; index < count; ++index)
        {
            int section = sections + index * 40;
            uint address = U32(bytes, section + 12), length = U32(bytes, section + 16);
            if (rva >= address && rva - address < length)
                return checked((int)(U32(bytes, section + 20) + rva - address));
        }
        throw new InvalidOperationException("Invalid managed PE address.");
    }
    static void SortRows(byte[] bytes, int start, int count, int rowSize, int ownerOffset, int ownerSize)
    {
        var rows = new List<byte[]>(count);
        for (int index = 0; index < count; ++index)
        {
            var row = new byte[rowSize];
            Buffer.BlockCopy(bytes, start + index * rowSize, row, 0, rowSize);
            rows.Add(row);
        }
        rows.Sort(delegate(byte[] left, byte[] right)
        {
            uint leftOwner = ownerSize == 2 ? U16(left, ownerOffset) : U32(left, ownerOffset);
            uint rightOwner = ownerSize == 2 ? U16(right, ownerOffset) : U32(right, ownerOffset);
            int comparison = leftOwner.CompareTo(rightOwner);
            if (comparison != 0) return comparison;
            // The full row provides a total ordering. Equal rows are identical.
            for (int index = 0; index < rowSize; ++index)
            {
                comparison = left[index].CompareTo(right[index]);
                if (comparison != 0) return comparison;
            }
            return 0;
        });
        for (int index = 0; index < count; ++index)
            Buffer.BlockCopy(rows[index], 0, bytes, start + index * rowSize, rowSize);
    }

    public static void Canonicalize(byte[] bytes)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("A little-endian build host is required.");
        int pe = checked((int)U32(bytes, 0x3c));
        if (U32(bytes, pe) != 0x00004550) throw new InvalidOperationException("Invalid PE signature.");
        int optional = pe + 24, sections = optional + U16(bytes, pe + 20);
        int directory = optional + (U16(bytes, optional) == 0x10b ? 96 : 112);
        int count = U16(bytes, pe + 6);
        int cli = FileOffset(bytes, U32(bytes, directory + 14 * 8), sections, count);
        int metadata = FileOffset(bytes, U32(bytes, cli + 8), sections, count);
        if (U32(bytes, metadata) != 0x424a5342) throw new InvalidOperationException("Invalid CLI metadata signature.");
        int header = checked(metadata + 16 + (int)U32(bytes, metadata + 12));
        header = (header + 3) & ~3;
        int streams = U16(bytes, header + 2), tables = -1;
        header += 4;
        for (int stream = 0; stream < streams; ++stream)
        {
            int name = header + 8, end = name;
            while (bytes[end] != 0) ++end;
            if (end - name == 2 && bytes[name] == '#' && bytes[name + 1] == '~')
                tables = checked(metadata + (int)U32(bytes, header));
            header = (end + 4) & ~3;
        }
        if (tables < 0) throw new InvalidOperationException("Compressed metadata tables were not found.");
        var rowCounts = new uint[64];
        int cursor = tables + 24;
        for (int table = 0; table < rowCounts.Length; ++table)
            if ((bytes[tables + 8 + table / 8] & (1 << (table % 8))) != 0)
            { rowCounts[table] = U32(bytes, cursor); cursor += 4; }
        int strings = (bytes[tables + 6] & 1) != 0 ? 4 : 2;
        int guids = (bytes[tables + 6] & 2) != 0 ? 4 : 2;
        int blobs = (bytes[tables + 6] & 4) != 0 ? 4 : 2;
        int type = Coded(rowCounts, 2, 2, 1, 27);
        int field = Index(rowCounts, 4), method = Index(rowCounts, 6);
        int parameter = Index(rowCounts, 8), typeDef = Index(rowCounts, 2);
        int property = Index(rowCounts, 23), eventIndex = Index(rowCounts, 20);
        int attributeOwner = Coded(rowCounts, 5, 6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43);
        int semanticsOwner = Coded(rowCounts, 1, 20, 23);
        // Row sizes through MethodSemantics, in ECMA-335 table-number order.
        int[] sizes = {
            2 + strings + 3 * guids,
            Coded(rowCounts, 2, 0, 26, 35, 1) + 2 * strings,
            4 + 2 * strings + type + field + method, field,
            2 + strings + blobs, method,
            8 + strings + blobs + parameter, parameter, 4 + strings,
            typeDef + type, Coded(rowCounts, 3, 2, 1, 26, 6, 27) + strings + blobs,
            2 + Coded(rowCounts, 2, 4, 8, 23) + blobs,
            attributeOwner + Coded(rowCounts, 3, 6, 10) + blobs,
            Coded(rowCounts, 1, 4, 8) + blobs,
            2 + Coded(rowCounts, 2, 2, 6, 32) + blobs,
            6 + typeDef, 4 + field, blobs, typeDef + eventIndex, eventIndex,
            2 + strings + type, typeDef + property, property, 2 + strings + blobs,
            2 + method + semanticsOwner
        };
        for (int table = 0; table < sizes.Length; ++table)
        {
            int rowCount = checked((int)rowCounts[table]);
            if (table == 12) SortRows(bytes, cursor, rowCount, sizes[table], 0, attributeOwner);
            if (table == 24) SortRows(bytes, cursor, rowCount, sizes[table], 2 + method, semanticsOwner);
            cursor = checked(cursor + rowCount * sizes[table]);
        }
    }
}
