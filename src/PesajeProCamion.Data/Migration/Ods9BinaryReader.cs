using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PesajeProCamion.Data.Migration;

/// <summary>
/// Lector binario de sólo lectura para bases de datos legacy InterBase/Firebird ODS 9.1 (*.GDB),
/// permitiendo auditar y migrar tablas sin requerir el motor legacy InterBase 5.x/BDE.
/// </summary>
public sealed class Ods9BinaryReader
{
    private const byte PagPointer = 4;
    private const byte PagData = 5;
    private const byte PagBlob = 8;

    private const ushort RhdDeleted = 0x0001;
    private const ushort RhdChain = 0x0002;
    private const ushort RhdFragment = 0x0004;
    private const ushort RhdIncomplete = 0x0008;
    private const ushort RhdBlob = 0x0010;

    private static readonly DateTime InterBaseEpoch = new(1858, 11, 17, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly Encoding Cp1252;

    static Ods9BinaryReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1252 = Encoding.GetEncoding(1252);
    }

    private readonly byte[] _raw;
    private readonly int _pageSize;
    private readonly int _numPages;
    private readonly int _maxRecsPerPage;
    private readonly Dictionary<int, List<(uint Seq, int PageNo, ushort Count)>> _pointerPages = new();

    public Ods9BinaryReader(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _raw = File.ReadAllBytes(path);
        if (_raw.Length < 1024)
        {
            throw new InvalidDataException("El archivo GDB es demasiado pequeño para contener una cabecera ODS válida.");
        }

        _pageSize = BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(16, 2));
        OdsMajor = BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(18, 2));
        OdsMinor = BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(62, 2));
        OldestTransaction = BinaryPrimitives.ReadInt32LittleEndian(_raw.AsSpan(28, 4));
        OldestActive = BinaryPrimitives.ReadInt32LittleEndian(_raw.AsSpan(32, 4));
        NextTransaction = BinaryPrimitives.ReadInt32LittleEndian(_raw.AsSpan(36, 4));

        _numPages = _raw.Length / _pageSize;
        _maxRecsPerPage = (_pageSize - 24) / 17;

        for (int p = 0; p < _numPages; p++)
        {
            ReadOnlySpan<byte> pg = GetPage(p);
            if (pg[0] == PagPointer)
            {
                uint seq = BinaryPrimitives.ReadUInt32LittleEndian(pg.Slice(16, 4));
                ushort cnt = BinaryPrimitives.ReadUInt16LittleEndian(pg.Slice(24, 2));
                ushort rel = BinaryPrimitives.ReadUInt16LittleEndian(pg.Slice(26, 2));
                if (!_pointerPages.TryGetValue(rel, out List<(uint, int, ushort)>? list))
                {
                    list = [];
                    _pointerPages[rel] = list;
                }
                list.Add((seq, p, cnt));
            }
        }

        foreach (List<(uint Seq, int PageNo, ushort Count)> list in _pointerPages.Values)
        {
            list.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        }
    }

    public int PageSize => _pageSize;
    public int NumPages => _numPages;
    public ushort OdsMajor { get; }
    public ushort OdsMinor { get; }
    public int OldestTransaction { get; }
    public int OldestActive { get; }
    public int NextTransaction { get; }

    private ReadOnlySpan<byte> GetPage(int pageNo)
        => _raw.AsSpan(pageNo * _pageSize, _pageSize);

    public Dictionary<string, List<Dictionary<string, object?>>> ReadAllUserTables()
    {
        Dictionary<short, string> relations = ReadRelationNames();
        Dictionary<(short RelId, byte Fmt), List<FieldDesc>> formats = ReadFormats();
        Dictionary<string, List<(short Fid, string ColName)>> relCols = ReadRelationColumns(formats);

        var result = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
        foreach ((short relId, string relName) in relations.OrderBy(kv => kv.Key))
        {
            if (relId < 128)
            {
                continue;
            }

            List<string> colNames = relCols.TryGetValue(relName, out List<(short Fid, string ColName)>? cols)
                ? cols.OrderBy(c => c.Fid).Select(c => c.ColName).ToList()
                : [];

            List<Dictionary<string, object?>> tableRows = [];
            (List<RawRow> rows, Dictionary<uint, byte[]> blobs) = ExtractRelationRecords(relId);

            foreach (RawRow row in rows)
            {
                if (!formats.TryGetValue((relId, row.Format), out List<FieldDesc>? descs) &&
                    !formats.TryGetValue((relId, 1), out descs) &&
                    !formats.TryGetValue((relId, 0), out descs))
                {
                    continue;
                }

                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int fid = 0; fid < descs.Count; fid++)
                {
                    FieldDesc d = descs[fid];
                    string colName = fid < colNames.Count ? colNames[fid] : $"COL_{fid}";
                    int bIdx = fid / 8;
                    int bMask = 1 << (fid % 8);
                    bool isNull = bIdx >= row.Data.Length || (row.Data[bIdx] & bMask) != 0;

                    if (isNull || d.Dtype == 0 || d.Address + d.Length > row.Data.Length)
                    {
                        dict[colName] = null;
                    }
                    else
                    {
                        dict[colName] = DecodeValue(row.Data.AsSpan(checked((int)d.Address), d.Length), d, blobs);
                    }
                }
                tableRows.Add(dict);
            }

            result[relName] = tableRows;
        }

        return result;
    }

    private Dictionary<short, string> ReadRelationNames()
    {
        var map = new Dictionary<short, string>();
        (List<RawRow> rows, _) = ExtractRelationRecords(6);
        foreach (RawRow row in rows)
        {
            if (row.Data.Length >= 69)
            {
                short rid = BinaryPrimitives.ReadInt16LittleEndian(row.Data.AsSpan(28, 2));
                string rname = Cp1252.GetString(row.Data, 38, 31).TrimEnd(' ', '\0');
                map[rid] = rname;
            }
        }
        return map;
    }

    private Dictionary<(short RelId, byte Fmt), List<FieldDesc>> ReadFormats()
    {
        var map = new Dictionary<(short, byte), List<FieldDesc>>();
        (List<RawRow> rows, Dictionary<uint, byte[]> blobs) = ExtractRelationRecords(8);
        foreach (RawRow row in rows)
        {
            if (row.Data.Length < 16)
            {
                continue;
            }
            short rid = BinaryPrimitives.ReadInt16LittleEndian(row.Data.AsSpan(4, 2));
            short fver = BinaryPrimitives.ReadInt16LittleEndian(row.Data.AsSpan(6, 2));
            uint p2 = BinaryPrimitives.ReadUInt32LittleEndian(row.Data.AsSpan(12, 4));
            if (blobs.TryGetValue(p2, out byte[]? bdata))
            {
                var descs = new List<FieldDesc>();
                for (int off = 0; off + 12 <= bdata.Length; off += 12)
                {
                    descs.Add(new FieldDesc(
                        bdata[off],
                        unchecked((sbyte)bdata[off + 1]),
                        BinaryPrimitives.ReadUInt16LittleEndian(bdata.AsSpan(off + 2, 2)),
                        BinaryPrimitives.ReadInt16LittleEndian(bdata.AsSpan(off + 4, 2)),
                        BinaryPrimitives.ReadUInt16LittleEndian(bdata.AsSpan(off + 6, 2)),
                        BinaryPrimitives.ReadUInt32LittleEndian(bdata.AsSpan(off + 8, 4))));
                }
                map[(rid, checked((byte)fver))] = descs;
            }
        }
        return map;
    }

    private Dictionary<string, List<(short Fid, string ColName)>> ReadRelationColumns(
        Dictionary<(short RelId, byte Fmt), List<FieldDesc>> formats)
    {
        var result = new Dictionary<string, List<(short, string)>>(StringComparer.OrdinalIgnoreCase);
        (List<RawRow> rows, _) = ExtractRelationRecords(5);
        foreach (RawRow row in rows)
        {
            if (!formats.TryGetValue((5, row.Format), out List<FieldDesc>? descs) &&
                !formats.TryGetValue((5, 1), out descs))
            {
                continue;
            }

            string colName = DecodeStringField(row.Data, descs, 0);
            string relName = DecodeStringField(row.Data, descs, 1);
            short fid = DecodeShortField(row.Data, descs, 9);
            if (!string.IsNullOrEmpty(relName) && !relName.StartsWith("RDB$", StringComparison.Ordinal))
            {
                if (!result.TryGetValue(relName, out List<(short, string)>? cols))
                {
                    cols = [];
                    result[relName] = cols;
                }
                cols.Add((fid, colName));
            }
        }
        return result;
    }

    private static string DecodeStringField(byte[] data, List<FieldDesc> descs, int idx)
    {
        if (idx >= descs.Count)
        {
            return string.Empty;
        }
        FieldDesc d = descs[idx];
        int bIdx = idx / 8;
        if (bIdx >= data.Length || (data[bIdx] & (1 << (idx % 8))) != 0)
        {
            return string.Empty;
        }
        int addr = checked((int)d.Address);
        return addr + d.Length <= data.Length ? Cp1252.GetString(data, addr, d.Length).TrimEnd(' ', '\0') : string.Empty;
    }

    private static short DecodeShortField(byte[] data, List<FieldDesc> descs, int idx)
    {
        if (idx >= descs.Count)
        {
            return 0;
        }
        FieldDesc d = descs[idx];
        int bIdx = idx / 8;
        if (bIdx >= data.Length || (data[bIdx] & (1 << (idx % 8))) != 0)
        {
            return 0;
        }
        int addr = checked((int)d.Address);
        return addr + 2 <= data.Length ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(addr, 2)) : (short)0;
    }

    private object? DecodeValue(ReadOnlySpan<byte> buf, FieldDesc d, Dictionary<uint, byte[]> inpageBlobs)
    {
        switch (d.Dtype)
        {
            case 1: // CHAR
                return Cp1252.GetString(buf).TrimEnd(' ', '\0');
            case 2: // CSTRING
            {
                int zero = buf.IndexOf((byte)0);
                return Cp1252.GetString(zero >= 0 ? buf[..zero] : buf);
            }
            case 3: // VARCHAR
            {
                ushort slen = BinaryPrimitives.ReadUInt16LittleEndian(buf);
                int len = Math.Min(slen, buf.Length - 2);
                return len > 0 ? Cp1252.GetString(buf.Slice(2, len)) : string.Empty;
            }
            case 8: // SMALLINT
            {
                short v = BinaryPrimitives.ReadInt16LittleEndian(buf);
                return d.Scale < 0 ? v * Math.Pow(10, d.Scale) : v;
            }
            case 9: // INTEGER
            {
                int v = BinaryPrimitives.ReadInt32LittleEndian(buf);
                return d.Scale < 0 ? v * Math.Pow(10, d.Scale) : v;
            }
            case 10: // QUAD
                return BinaryPrimitives.ReadInt64LittleEndian(buf);
            case 11: // FLOAT
                return (double)BinaryPrimitives.ReadSingleLittleEndian(buf);
            case 12: // DOUBLE
                return BinaryPrimitives.ReadDoubleLittleEndian(buf);
            case 14: // TIMESTAMP / DATE (8 bytes in ODS9 or 4 bytes)
            case 15:
            case 16:
                if (buf.Length == 8)
                {
                    int days = BinaryPrimitives.ReadInt32LittleEndian(buf[..4]);
                    int ticks = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(4, 4));
                    if (days == 0 && ticks == 0)
                    {
                        return null;
                    }
                    return InterBaseEpoch.AddDays(days).AddTicks(ticks * 1000L);
                }
                if (buf.Length == 4)
                {
                    int days = BinaryPrimitives.ReadInt32LittleEndian(buf);
                    return InterBaseEpoch.AddDays(days);
                }
                return null;
            case 17: // BLOB ID
            {
                uint p1 = BinaryPrimitives.ReadUInt32LittleEndian(buf[..4]);
                uint p2 = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(4, 4));
                if (p1 == 0 && p2 == 0)
                {
                    return null;
                }
                if (inpageBlobs.TryGetValue(p2, out byte[]? bdata))
                {
                    return Cp1252.GetString(bdata);
                }
                if (p2 > 0 && p2 < (uint)_numPages && GetPage(checked((int)p2))[0] == PagBlob)
                {
                    return Cp1252.GetString(ReadPageBlob(checked((int)p2)));
                }
                return string.Empty;
            }
            case 19:
            {
                long v = BinaryPrimitives.ReadInt64LittleEndian(buf);
                return d.Scale < 0 ? v * Math.Pow(10, d.Scale) : v;
            }
            default:
                return null;
        }
    }

    private byte[] ReadPageBlob(int leadPage)
    {
        using var ms = new MemoryStream();
        for (int p = leadPage; p < Math.Min(_numPages, leadPage + 50); p++)
        {
            ReadOnlySpan<byte> pg = GetPage(p);
            if (pg[0] != PagBlob)
            {
                continue;
            }
            uint lead = BinaryPrimitives.ReadUInt32LittleEndian(pg.Slice(16, 4));
            if (lead != leadPage && !(p == leadPage && lead == 0))
            {
                continue;
            }
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(pg.Slice(24, 2));
            ReadOnlySpan<byte> payload = pg.Slice(26, Math.Min(len, _pageSize - 26));
            if ((pg[1] & 1) != 0)
            {
                ms.Write(payload);
            }
            else
            {
                int pos = 0;
                while (pos + 2 <= payload.Length)
                {
                    ushort slen = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(pos, 2));
                    pos += 2;
                    if (pos + slen <= payload.Length)
                    {
                        ms.Write(payload.Slice(pos, slen));
                        ms.WriteByte((byte)'\n');
                        pos += slen;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        return ms.ToArray();
    }

    private (List<RawRow> Rows, Dictionary<uint, byte[]> Blobs) ExtractRelationRecords(short relId)
    {
        var rows = new List<RawRow>();
        var blobs = new Dictionary<uint, byte[]>();
        if (!_pointerPages.TryGetValue(relId, out List<(uint Seq, int PageNo, ushort Count)>? ptrList))
        {
            return (rows, blobs);
        }

        foreach ((_, int ptrPage, ushort count) in ptrList)
        {
            ReadOnlySpan<byte> pp = GetPage(ptrPage);
            for (int i = 0; i < count; i++)
            {
                int dp = BinaryPrimitives.ReadInt32LittleEndian(pp.Slice(32 + i * 4, 4));
                if (dp <= 0 || dp >= _numPages)
                {
                    continue;
                }

                ReadOnlySpan<byte> dpg = GetPage(dp);
                if (dpg[0] != PagData)
                {
                    continue;
                }

                uint dpgSeq = BinaryPrimitives.ReadUInt32LittleEndian(dpg.Slice(16, 4));
                ushort rcnt = BinaryPrimitives.ReadUInt16LittleEndian(dpg.Slice(22, 2));
                for (int line = 0; line < rcnt; line++)
                {
                    ushort off = BinaryPrimitives.ReadUInt16LittleEndian(dpg.Slice(24 + line * 4, 2));
                    ushort ln = BinaryPrimitives.ReadUInt16LittleEndian(dpg.Slice(26 + line * 4, 2));
                    if (ln < 13 || off == 0 || off + ln > _pageSize)
                    {
                        continue;
                    }

                    ReadOnlySpan<byte> slot = dpg.Slice(off, ln);
                    ushort bl = BinaryPrimitives.ReadUInt16LittleEndian(slot.Slice(8, 2));
                    ushort fl = BinaryPrimitives.ReadUInt16LittleEndian(slot.Slice(10, 2));
                    byte fmt = slot[12];

                    if ((fl & RhdBlob) != 0)
                    {
                        int payloadLen = Math.Min(bl, slot.Length - 30);
                        if (payloadLen > 0)
                        {
                            uint recNum = checked((uint)(dpgSeq * _maxRecsPerPage + line));
                            blobs[recNum] = slot.Slice(30, payloadLen).ToArray();
                        }
                        continue;
                    }

                    if ((fl & (RhdFragment | RhdChain | RhdDeleted)) != 0)
                    {
                        continue;
                    }

                    byte[] decompressed;
                    if ((fl & RhdIncomplete) != 0 && slot.Length >= 22)
                    {
                        using var ms = new MemoryStream();
                        WriteRleDecompressed(slot[22..], ms);
                        uint curP = BinaryPrimitives.ReadUInt32LittleEndian(slot.Slice(16, 4));
                        ushort curL = BinaryPrimitives.ReadUInt16LittleEndian(slot.Slice(20, 2));
                        int hops = 0;
                        while (curP > 0 && curP < (uint)_numPages && hops++ < 50)
                        {
                            ReadOnlySpan<byte> frag = FetchSlot(checked((int)curP), curL);
                            if (frag.Length < 13)
                            {
                                break;
                            }
                            ushort ffl = BinaryPrimitives.ReadUInt16LittleEndian(frag.Slice(10, 2));
                            if ((ffl & RhdIncomplete) != 0 && frag.Length >= 22)
                            {
                                WriteRleDecompressed(frag[22..], ms);
                                curP = BinaryPrimitives.ReadUInt32LittleEndian(frag.Slice(16, 4));
                                curL = BinaryPrimitives.ReadUInt16LittleEndian(frag.Slice(20, 2));
                            }
                            else
                            {
                                WriteRleDecompressed(frag[13..], ms);
                                break;
                            }
                        }
                        decompressed = ms.ToArray();
                    }
                    else
                    {
                        using var ms = new MemoryStream();
                        WriteRleDecompressed(slot[13..], ms);
                        decompressed = ms.ToArray();
                    }

                    rows.Add(new RawRow(fmt, decompressed));
                }
            }
        }

        return (rows, blobs);
    }

    private ReadOnlySpan<byte> FetchSlot(int dp, ushort line)
    {
        if (dp <= 0 || dp >= _numPages)
        {
            return [];
        }
        ReadOnlySpan<byte> pg = GetPage(dp);
        if (pg[0] != PagData)
        {
            return [];
        }
        ushort cnt = BinaryPrimitives.ReadUInt16LittleEndian(pg.Slice(22, 2));
        if (line >= cnt)
        {
            return [];
        }
        ushort off = BinaryPrimitives.ReadUInt16LittleEndian(pg.Slice(24 + line * 4, 2));
        ushort ln = BinaryPrimitives.ReadUInt16LittleEndian(pg.Slice(26 + line * 4, 2));
        return ln == 0 || off == 0 || off + ln > _pageSize ? [] : pg.Slice(off, ln);
    }

    private static void WriteRleDecompressed(ReadOnlySpan<byte> buf, MemoryStream ms)
    {
        int i = 0;
        while (i < buf.Length)
        {
            sbyte c = unchecked((sbyte)buf[i++]);
            if (c > 0)
            {
                int count = Math.Min(c, buf.Length - i);
                ms.Write(buf.Slice(i, count));
                i += count;
            }
            else if (c < 0)
            {
                int count = -c;
                if (i < buf.Length)
                {
                    byte b = buf[i++];
                    for (int k = 0; k < count; k++)
                    {
                        ms.WriteByte(b);
                    }
                }
            }
            else
            {
                break;
            }
        }
    }

    private readonly record struct FieldDesc(byte Dtype, sbyte Scale, ushort Length, short SubType, ushort Flags, uint Address);
    private readonly record struct RawRow(byte Format, byte[] Data);
}
