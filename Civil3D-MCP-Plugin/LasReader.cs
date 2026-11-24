using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcpPlugin;

public sealed record LasThinningResult(
  string Version,
  int PointFormat,
  ulong TotalPoints,
  ulong MatchedPoints,
  SortedDictionary<int, ulong> ClassCounts,
  List<Point3d> Points);

// Minimal reader for uncompressed ASPRS LAS 1.0–1.4 (point formats 0–10). The Civil 3D .NET API cannot
// build a surface from a point cloud (no point-cloud surface definition; PointCloudEx exposes no
// points), so the file is read directly, filtered by classification and thinned to one point per grid
// cell before being added to a TIN.
public static class LasReader
{
  private const int ChunkPoints = 65536;

  private struct Cell
  {
    public double SumX;
    public double SumY;
    public double SumZ;
    public int Count;
    public double MinX;
    public double MinY;
    public double MinZ;
  }

  public static LasThinningResult ReadThinned(
    string path,
    IReadOnlySet<int> classifications,
    double gridSpacing,
    bool keepLowest,
    int maxPoints,
    CancellationToken cancellationToken)
  {
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
    using var reader = new BinaryReader(stream);

    var signature = new string(reader.ReadChars(4));
    if (signature != "LASF")
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"'{path}' is not a LAS file (signature '{signature}').");
    }

    stream.Position = 24;
    var versionMajor = reader.ReadByte();
    var versionMinor = reader.ReadByte();
    stream.Position = 96;
    var offsetToPoints = reader.ReadUInt32();
    stream.Position = 104;
    var formatByte = reader.ReadByte();
    if ((formatByte & 0x80) != 0)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Compressed point data (LAZ) is not supported: decompress to .las first.");
    }

    var pointFormat = formatByte & 0x3F;
    if (pointFormat > 10)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Unsupported LAS point format {pointFormat}.");
    }

    var recordLength = reader.ReadUInt16();
    ulong pointCount = reader.ReadUInt32();
    stream.Position = 131;
    var scaleX = reader.ReadDouble();
    var scaleY = reader.ReadDouble();
    var scaleZ = reader.ReadDouble();
    var offsetX = reader.ReadDouble();
    var offsetY = reader.ReadDouble();
    var offsetZ = reader.ReadDouble();
    stream.Position = 187;
    var minX = reader.ReadDouble();
    stream.Position = 203;
    var minY = reader.ReadDouble();
    if (versionMajor == 1 && versionMinor >= 4)
    {
      stream.Position = 247;
      var extendedCount = reader.ReadUInt64();
      if (extendedCount > 0)
      {
        pointCount = extendedCount;
      }
    }

    // Formats 0–5 keep the class in the low 5 bits of byte 15; formats 6–10 use the whole byte 16.
    var classOffset = pointFormat >= 6 ? 16 : 15;
    var classMask = pointFormat >= 6 ? 0xFF : 0x1F;
    var filterAll = classifications.Count == 0;
    var cells = new Dictionary<long, Cell>();
    var classCounts = new SortedDictionary<int, ulong>();
    ulong matched = 0;

    stream.Position = offsetToPoints;
    var buffer = new byte[recordLength * ChunkPoints];
    ulong remaining = pointCount;
    while (remaining > 0)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var batch = (int)Math.Min((ulong)ChunkPoints, remaining);
      var bytes = batch * recordLength;
      var read = 0;
      while (read < bytes)
      {
        var n = stream.Read(buffer, read, bytes - read);
        if (n == 0)
        {
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "LAS file ended before the declared number of points.");
        }

        read += n;
      }

      for (var i = 0; i < batch; i++)
      {
        var offset = i * recordLength;
        var classification = buffer[offset + classOffset] & classMask;
        classCounts[classification] = classCounts.TryGetValue(classification, out var c) ? c + 1 : 1;
        if (!filterAll && !classifications.Contains(classification))
        {
          continue;
        }

        matched++;
        var x = BitConverter.ToInt32(buffer, offset) * scaleX + offsetX;
        var y = BitConverter.ToInt32(buffer, offset + 4) * scaleY + offsetY;
        var z = BitConverter.ToInt32(buffer, offset + 8) * scaleZ + offsetZ;
        var ix = (long)Math.Floor((x - minX) / gridSpacing);
        var iy = (long)Math.Floor((y - minY) / gridSpacing);
        var key = (ix << 32) ^ (iy & 0xFFFFFFFF);

        if (cells.TryGetValue(key, out var cell))
        {
          cell.SumX += x;
          cell.SumY += y;
          cell.SumZ += z;
          cell.Count++;
          if (z < cell.MinZ)
          {
            cell.MinX = x;
            cell.MinY = y;
            cell.MinZ = z;
          }

          cells[key] = cell;
        }
        else
        {
          if (cells.Count >= maxPoints)
          {
            throw new JsonRpcDispatchException(
              "CIVIL3D.INVALID_INPUT",
              $"More than {maxPoints} grid cells at gridSpacing={gridSpacing}: increase gridSpacing or maxPoints.");
          }

          cells[key] = new Cell { SumX = x, SumY = y, SumZ = z, Count = 1, MinX = x, MinY = y, MinZ = z };
        }
      }

      remaining -= (ulong)batch;
    }

    var points = cells.Values
      .Select(cell => keepLowest
        ? new Point3d(cell.MinX, cell.MinY, cell.MinZ)
        : new Point3d(cell.SumX / cell.Count, cell.SumY / cell.Count, cell.SumZ / cell.Count))
      .ToList();

    return new LasThinningResult($"{versionMajor}.{versionMinor}", pointFormat, pointCount, matched, classCounts, points);
  }
}
