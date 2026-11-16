using System.Text;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Минимальный read-only парсер контейнеров OLE2 Compound File Binary Format (CFBF),
/// общий для <c>.doc</c> и <c>.xls</c>.
/// </summary>
internal sealed class Ole2CompoundFile
{
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSect = 0xFFFFFFFF;

    private readonly byte[] _data;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly uint _miniCutoff;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly byte[] _miniStream;
    private readonly Dictionary<string, (uint StartSector, ulong Size)> _streams;

    private Ole2CompoundFile(
        byte[] data,
        int sectorSize,
        int miniSectorSize,
        uint miniCutoff,
        uint[] fat,
        uint[] miniFat,
        byte[] miniStream,
        Dictionary<string, (uint, ulong)> streams)
    {
        _data = data;
        _sectorSize = sectorSize;
        _miniSectorSize = miniSectorSize;
        _miniCutoff = miniCutoff;
        _fat = fat;
        _miniFat = miniFat;
        _miniStream = miniStream;
        _streams = streams;
    }

    public static Ole2CompoundFile Parse(byte[] data)
    {
        if (data.Length < 512)
            throw new InvalidDataException("Invalid OLE2 header.");

        var majorVersion = BitConverter.ToUInt16(data, 0x1A);
        var sectorShift = BitConverter.ToUInt16(data, 0x1E);
        var miniSectorShift = BitConverter.ToUInt16(data, 0x20);
        var sectorSize = 1 << sectorShift;
        var miniSectorSize = 1 << miniSectorShift;
        var numFatSectors = (int)BitConverter.ToUInt32(data, 0x2C);
        var firstDirSector = BitConverter.ToUInt32(data, 0x30);
        var miniCutoff = BitConverter.ToUInt32(data, 0x38);
        var firstMiniFatSector = BitConverter.ToUInt32(data, 0x3C);
        var firstDifatSector = BitConverter.ToUInt32(data, 0x44);

        var fatSectorIds = new List<uint>(Math.Max(0, numFatSectors));
        for (var i = 0; i < 109 && fatSectorIds.Count < numFatSectors; i++)
        {
            var sid = BitConverter.ToUInt32(data, 0x4C + i * 4);
            if (sid != FreeSect)
                fatSectorIds.Add(sid);
        }

        var difatSector = firstDifatSector;
        var difatGuard = 0;
        while (difatSector < EndOfChain && fatSectorIds.Count < numFatSectors && difatGuard++ < 1024)
        {
            var off = SectorOffset(difatSector, sectorSize);
            if (off < 0 || off + sectorSize > data.Length)
                break;

            var entriesPerSector = sectorSize / 4 - 1;
            for (var i = 0; i < entriesPerSector && fatSectorIds.Count < numFatSectors; i++)
            {
                var sid = BitConverter.ToUInt32(data, off + i * 4);
                if (sid != FreeSect)
                    fatSectorIds.Add(sid);
            }

            difatSector = BitConverter.ToUInt32(data, off + entriesPerSector * 4);
        }

        var fat = new uint[fatSectorIds.Count * (sectorSize / 4)];
        for (var s = 0; s < fatSectorIds.Count; s++)
        {
            var off = SectorOffset(fatSectorIds[s], sectorSize);
            if (off < 0 || off + sectorSize > data.Length)
                continue;

            var count = sectorSize / 4;
            for (var i = 0; i < count; i++)
                fat[s * count + i] = BitConverter.ToUInt32(data, off + i * 4);
        }

        var dirBytes = ReadChain(data, fat, firstDirSector, sectorSize);
        var streams = new Dictionary<string, (uint StartSector, ulong Size)>(StringComparer.OrdinalIgnoreCase);
        uint rootStart = EndOfChain;
        ulong rootSize = 0;

        for (var pos = 0; pos + 128 <= dirBytes.Length; pos += 128)
        {
            var objType = dirBytes[pos + 0x42];
            var nameBytes = BitConverter.ToUInt16(dirBytes, pos + 0x40);
            if (objType == 0 || nameBytes < 2 || nameBytes > 64)
                continue;

            var name = Encoding.Unicode.GetString(dirBytes, pos, nameBytes - 2);
            var startSector = BitConverter.ToUInt32(dirBytes, pos + 0x74);
            var size = majorVersion == 3
                ? BitConverter.ToUInt32(dirBytes, pos + 0x78)
                : BitConverter.ToUInt64(dirBytes, pos + 0x78);

            if (objType == 5)
            {
                rootStart = startSector;
                rootSize = size;
            }
            else if (objType == 2)
            {
                streams[name] = (startSector, size);
            }
        }

        var miniFatBytes = firstMiniFatSector < EndOfChain
            ? ReadChain(data, fat, firstMiniFatSector, sectorSize)
            : [];
        var miniFat = new uint[miniFatBytes.Length / 4];
        for (var i = 0; i < miniFat.Length; i++)
            miniFat[i] = BitConverter.ToUInt32(miniFatBytes, i * 4);

        var miniStream = rootStart < EndOfChain
            ? ReadChain(data, fat, rootStart, sectorSize, rootSize)
            : [];

        return new Ole2CompoundFile(data, sectorSize, miniSectorSize, miniCutoff, fat, miniFat, miniStream, streams);
    }

    public byte[]? ReadStream(string name)
    {
        if (!_streams.TryGetValue(name, out var entry))
            return null;

        return entry.Size < _miniCutoff
            ? ReadMiniChain(_miniStream, _miniFat, entry.StartSector, _miniSectorSize, entry.Size)
            : ReadChain(_data, _fat, entry.StartSector, _sectorSize, entry.Size);
    }

    private static int SectorOffset(uint sectorId, int sectorSize)
        => checked((int)((sectorId + 1UL) * (ulong)sectorSize));

    private static byte[] ReadChain(byte[] data, uint[] fat, uint startSector, int sectorSize, ulong? maxLength = null)
    {
        using var ms = new MemoryStream();
        var visited = new HashSet<uint>();
        var sector = startSector;

        while (sector < EndOfChain && visited.Add(sector))
        {
            var off = SectorOffset(sector, sectorSize);
            if (off < 0 || off >= data.Length)
                break;

            var len = Math.Min(sectorSize, data.Length - off);
            ms.Write(data, off, len);

            if (maxLength is not null && (ulong)ms.Length >= maxLength.Value)
                break;

            sector = sector < (uint)fat.Length ? fat[sector] : EndOfChain;
        }

        var bytes = ms.ToArray();
        if (maxLength is not null && (ulong)bytes.Length > maxLength.Value)
            Array.Resize(ref bytes, (int)maxLength.Value);

        return bytes;
    }

    private static byte[] ReadMiniChain(byte[] miniStream, uint[] miniFat, uint startSector, int miniSectorSize, ulong maxLength)
    {
        using var ms = new MemoryStream();
        var visited = new HashSet<uint>();
        var sector = startSector;

        while (sector < EndOfChain && visited.Add(sector))
        {
            var off = checked((int)(sector * (ulong)miniSectorSize));
            if (off < 0 || off >= miniStream.Length)
                break;

            var len = Math.Min(miniSectorSize, miniStream.Length - off);
            ms.Write(miniStream, off, len);

            if ((ulong)ms.Length >= maxLength)
                break;

            sector = sector < (uint)miniFat.Length ? miniFat[sector] : EndOfChain;
        }

        var bytes = ms.ToArray();
        if ((ulong)bytes.Length > maxLength)
            Array.Resize(ref bytes, (int)maxLength);

        return bytes;
    }
}
