using System.Text.Json.Nodes;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcpPlugin;

// ITZA additions to the surface domain (11-16): point groups, styles, analysis generation, labels
// and LAS point-cloud import. Every Civil 3D call here was checked against the AeccDbMgd 2026 metadata.
public static partial class SurfaceCommands
{
  public static Task<object?> AddSurfacePointGroupAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var pointGroupName = PluginRuntime.GetRequiredString(parameters, "pointGroup");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var surface = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, name, OpenMode.ForWrite);
      if (surface is not TinSurface tinSurface)
      {
        throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"Point groups can only be added to TIN surfaces, not '{surface.GetType().Name}'.");
      }

      if (!civilDoc.PointGroups.Contains(pointGroupName))
      {
        var available = new List<string>();
        foreach (ObjectId id in civilDoc.PointGroups)
        {
          available.Add(CivilObjectUtils.GetRequiredObject<PointGroup>(transaction, id, OpenMode.ForRead).Name);
        }

        throw new JsonRpcDispatchException(
          "CIVIL3D.OBJECT_NOT_FOUND",
          $"Point group '{pointGroupName}' was not found. Available: {string.Join(", ", available)}.");
      }

      var pointGroupId = civilDoc.PointGroups[pointGroupName];
      var pointGroup = CivilObjectUtils.GetRequiredObject<PointGroup>(transaction, pointGroupId, OpenMode.ForRead);
      tinSurface.PointGroupsDefinition.AddPointGroup(pointGroupId);
      tinSurface.Rebuild();

      var generalProperties = surface.GetGeneralProperties();
      return new Dictionary<string, object?>
      {
        ["surfaceName"] = surface.Name,
        ["pointGroup"] = pointGroup.Name,
        ["pointGroupPointCount"] = pointGroup.PointsCount,
        ["surfacePointCount"] = generalProperties.NumberOfPoints,
        ["numberOfTriangles"] = GetTriangleCount(surface),
        ["linked"] = true,
      };
    });
  }

  public static Task<object?> SetSurfaceStyleAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var styleName = PluginRuntime.GetRequiredString(parameters, "style");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var surface = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, name, OpenMode.ForWrite);
      var previousStyle = CivilObjectUtils.GetName(transaction.GetObject(surface.StyleId, OpenMode.ForRead));
      surface.StyleId = LookupUtils.GetRequiredNamedStyleId(civilDoc.Styles.SurfaceStyles, transaction, styleName, "Surface style");

      return new Dictionary<string, object?>
      {
        ["surfaceName"] = surface.Name,
        ["previousStyle"] = previousStyle,
        ["style"] = CivilObjectUtils.GetName(transaction.GetObject(surface.StyleId, OpenMode.ForRead)),
      };
    });
  }

  public static Task<object?> GenerateSurfaceAnalysisAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var analysisType = PluginRuntime.GetRequiredString(parameters, "analysisType").Trim().ToLowerInvariant();
    var numRanges = PluginRuntime.GetOptionalInt(parameters, "numRanges") ?? (analysisType == "directions" ? 8 : 6);
    if (numRanges is < 1 or > 64)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "numRanges must be between 1 and 64.");
    }

    var customRanges = (PluginRuntime.GetParameter(parameters, "ranges") as JsonArray)?
      .OfType<JsonObject>()
      .Select(range => (Min: range["min"]!.GetValue<double>(), Max: range["max"]!.GetValue<double>()))
      .ToList();
    if (customRanges is { Count: > 0 } && customRanges.Any(range => range.Max <= range.Min))
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Each range needs max > min.");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var surface = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, name, OpenMode.ForWrite);
      var generalProperties = surface.GetGeneralProperties();
      var terrainProperties = GetTerrainProperties(surface);
      List<(double Min, double Max)> ranges;
      string units;

      switch (analysisType)
      {
        case "elevations":
          ranges = customRanges is { Count: > 0 }
            ? customRanges
            : EqualRanges(generalProperties.MinimumElevation, generalProperties.MaximumElevation, numRanges);
          surface.Analysis.SetElevationData(ranges
            .Select((range, index) => new SurfaceAnalysisElevationData(range.Min, range.Max, RampColor(index, ranges.Count)))
            .ToArray());
          units = CivilObjectUtils.LinearUnits(database);
          break;

        case "slopes":
          // Ranges are taken in percent; the stored values use the same units as
          // TerrainSurfaceProperties.Minimum/MaximumGradeOrSlope (reported below to check them).
          var minSlope = terrainProperties?.MinimumGradeOrSlope ?? 0d;
          var maxSlope = terrainProperties?.MaximumGradeOrSlope ?? 0d;
          ranges = customRanges is { Count: > 0 }
            ? customRanges.Select(range => (range.Min / 100d, range.Max / 100d)).ToList()
            : EqualRanges(minSlope, maxSlope, numRanges);
          surface.Analysis.SetSlopeData(ranges
            .Select((range, index) => new SurfaceAnalysisSlopeData(range.Min, range.Max, RampColor(index, ranges.Count)))
            .ToArray());
          units = "ratio (rise/run)";
          break;

        case "directions":
          ranges = customRanges is { Count: > 0 }
            ? customRanges.Select(range => (range.Min * Math.PI / 180d, range.Max * Math.PI / 180d)).ToList()
            : EqualRanges(0d, 2d * Math.PI, numRanges);
          surface.Analysis.SetDirectionData(ranges
            .Select((range, index) => new SurfaceAnalysisDirectionData(range.Min, range.Max, RampColor(index, ranges.Count)))
            .ToArray());
          units = "radians";
          break;

        default:
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "analysisType must be 'elevations', 'slopes' or 'directions'.");
      }

      return new Dictionary<string, object?>
      {
        ["surfaceName"] = surface.Name,
        ["analysisType"] = analysisType,
        ["rangeCount"] = ranges.Count,
        ["ranges"] = ranges.Select(range => new Dictionary<string, object?> { ["min"] = range.Min, ["max"] = range.Max }).ToList(),
        ["storedUnits"] = units,
        ["surfaceMinimumGradeOrSlope"] = terrainProperties?.MinimumGradeOrSlope,
        ["surfaceMaximumGradeOrSlope"] = terrainProperties?.MaximumGradeOrSlope,
        ["note"] = "The ranges only show in the drawing with a surface style that displays this analysis (e.g. 'Elevation Banding (2D)' or 'Slope Banding (2D)'): use style_set.",
      };
    });
  }

  public static Task<object?> AddSurfaceLabelsAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var labelKind = PluginRuntime.GetRequiredString(parameters, "labelKind").Trim().ToLowerInvariant();
    var labelStyle = PluginRuntime.GetOptionalString(parameters, "labelStyle");
    var interval = PluginRuntime.GetOptionalDouble(parameters, "interval");
    var points = labelKind == "contour"
      ? null
      : ParseRequiredPoint2dArray(parameters, "points", $"label_add '{labelKind}' requires 'points'.");

    if (labelKind == "slope_two_point" && (points!.Count < 2 || points.Count % 2 != 0))
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "slope_two_point labels need an even number of points (pairs).");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var surface = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, name, OpenMode.ForRead);
      var surfaceId = surface.ObjectId;
      var labelStyles = civilDoc.Styles.LabelStyles.SurfaceLabelStyles;
      var created = new List<ObjectId>();

      switch (labelKind)
      {
        case "spot_elevation":
          var spotStyleId = string.IsNullOrWhiteSpace(labelStyle)
            ? ObjectId.Null
            : LookupUtils.GetRequiredNamedStyleId(labelStyles.SpotElevationLabelStyles, transaction, labelStyle, "Spot elevation label style");
          foreach (Point2d point in points!)
          {
            created.Add(SurfaceElevationLabel.Create(surfaceId, point));
          }

          ApplyLabelStyle(transaction, created, spotStyleId);
          break;

        case "slope":
        case "slope_two_point":
          var slopeStyleId = string.IsNullOrWhiteSpace(labelStyle)
            ? ObjectId.Null
            : LookupUtils.GetRequiredNamedStyleId(labelStyles.SlopeLabelStyles, transaction, labelStyle, "Slope label style");
          if (labelKind == "slope")
          {
            foreach (Point2d point in points!)
            {
              created.Add(SurfaceSlopeLabel.Create(surfaceId, point));
            }
          }
          else
          {
            for (var i = 0; i < points!.Count; i += 2)
            {
              created.Add(SurfaceSlopeLabel.Create(surfaceId, points[i], points[i + 1]));
            }
          }

          ApplyLabelStyle(transaction, created, slopeStyleId);
          break;

        case "contour":
          var start = ParseRequiredPoint2d(parameters, "startPoint");
          var end = ParseRequiredPoint2d(parameters, "endPoint");
          var before = CountContourLabelGroups(database, transaction, surfaceId);
          if (interval is double step)
          {
            if (step <= 0)
            {
              throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "interval must be positive.");
            }

            SurfaceContourLabelGroup.CreateMultipleAtInterval(surfaceId, start, end, step);
          }
          else
          {
            created.Add(SurfaceContourLabelGroup.Create(surfaceId, new Point2dCollection { start, end }));
          }

          var contourCount = CountContourLabelGroups(database, transaction, surfaceId) - before;
          if (!string.IsNullOrWhiteSpace(labelStyle))
          {
            var contourStyleId = LookupUtils.GetRequiredNamedStyleId(labelStyles.ContourLabelStyles, transaction, labelStyle, "Contour label style");
            foreach (var groupId in ContourLabelGroupIds(database, transaction, surfaceId).TakeLast(contourCount))
            {
              var group = CivilObjectUtils.GetRequiredObject<SurfaceContourLabelGroup>(transaction, groupId, OpenMode.ForWrite);
              group.MajorContourLabelStyleId = contourStyleId;
              group.MinorContourLabelStyleId = contourStyleId;
            }
          }

          return new Dictionary<string, object?>
          {
            ["surfaceName"] = surface.Name,
            ["labelKind"] = labelKind,
            ["labelLinesCreated"] = contourCount,
            ["labelStyle"] = labelStyle,
          };

        default:
          throw new JsonRpcDispatchException(
            "CIVIL3D.INVALID_INPUT",
            "labelKind must be 'spot_elevation', 'slope', 'slope_two_point' or 'contour'.");
      }

      return new Dictionary<string, object?>
      {
        ["surfaceName"] = surface.Name,
        ["labelKind"] = labelKind,
        ["labelsCreated"] = created.Count,
        ["handles"] = created.Select(id => id.Handle.ToString()).ToList(),
        ["labelStyle"] = labelStyle,
      };
    });
  }

  public static async Task<object?> CreateSurfaceFromLasAsync(JsonObject? parameters)
  {
    var filePath = FileBoundary.ResolveImportPath(PluginRuntime.GetRequiredString(parameters, "filePath"), ".las");
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var style = PluginRuntime.GetOptionalString(parameters, "style");
    var layer = PluginRuntime.GetOptionalString(parameters, "layer");
    var description = PluginRuntime.GetOptionalString(parameters, "description");
    var gridSpacing = PluginRuntime.GetOptionalDouble(parameters, "gridSpacing") ?? 1d;
    var thinning = (PluginRuntime.GetOptionalString(parameters, "thinning") ?? "mean").Trim().ToLowerInvariant();
    var maxPoints = PluginRuntime.GetOptionalInt(parameters, "maxPoints") ?? 2_000_000;
    var classesNode = PluginRuntime.GetParameter(parameters, "classifications") as JsonArray;
    var classifications = classesNode == null
      ? new HashSet<int> { 2 }
      : classesNode.Select(node => node!.GetValue<int>()).ToHashSet();
    var boundary = PluginRuntime.GetParameter(parameters, "boundary") is JsonArray
      ? ParseRequiredPoint2dArray(parameters, "boundary", "boundary needs at least 3 points.")
      : null;

    if (gridSpacing <= 0)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "gridSpacing must be positive.");
    }

    if (thinning is not ("mean" or "min"))
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "thinning must be 'mean' or 'min'.");
    }

    // Reading hundreds of millions of points must not block AutoCAD's main thread.
    var cancellationToken = PluginRuntime.GetCurrentRequestCancellationToken();
    var las = await Task.Run(
      () => LasReader.ReadThinned(filePath, classifications, gridSpacing, thinning == "min", maxPoints, cancellationToken),
      cancellationToken);

    var vertices = new Point3dCollection();
    foreach (var point in las.Points)
    {
      if (boundary == null || IsPointInPolygon(point.X, point.Y, boundary))
      {
        vertices.Add(point);
      }
    }

    if (vertices.Count < 3)
    {
      throw new JsonRpcDispatchException(
        "CIVIL3D.INVALID_INPUT",
        $"Only {vertices.Count} point(s) left after filtering (classes {string.Join(",", classifications)}, boundary). " +
        $"Classes present in the file: {string.Join(", ", las.ClassCounts.Select(pair => $"{pair.Key}={pair.Value}"))}.");
    }

    return await CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var styleId = LookupUtils.GetSurfaceStyleId(civilDoc, transaction, style);
      var surfaceId = CreateTinSurface(name, styleId);
      var surface = CivilObjectUtils.GetRequiredObject<TinSurface>(transaction, surfaceId, OpenMode.ForWrite);
      surface.AddVertices(vertices);
      ApplySurfaceLayer(surface, layer, civilDoc, database, transaction);
      if (!string.IsNullOrWhiteSpace(description))
      {
        surface.Description = description;
      }

      surface.Rebuild();
      var generalProperties = surface.GetGeneralProperties();
      return new Dictionary<string, object?>
      {
        ["name"] = surface.Name,
        ["handle"] = CivilObjectUtils.GetHandle(surface),
        ["filePath"] = filePath,
        ["lasVersion"] = las.Version,
        ["pointFormat"] = las.PointFormat,
        ["pointsInFile"] = las.TotalPoints,
        ["pointsMatchingClasses"] = las.MatchedPoints,
        ["classCounts"] = las.ClassCounts.ToDictionary(pair => pair.Key.ToString(), pair => (object?)pair.Value),
        ["gridSpacing"] = gridSpacing,
        ["thinning"] = thinning,
        ["verticesAdded"] = vertices.Count,
        ["surfacePointCount"] = generalProperties.NumberOfPoints,
        ["numberOfTriangles"] = GetTriangleCount(surface),
        ["minimumElevation"] = generalProperties.MinimumElevation,
        ["maximumElevation"] = generalProperties.MaximumElevation,
        ["created"] = true,
      };
    });
  }

  private static List<(double Min, double Max)> EqualRanges(double min, double max, int count)
  {
    if (!(max > min))
    {
      max = min + 1e-6;
    }

    var step = (max - min) / count;
    return Enumerable.Range(0, count)
      .Select(index => (min + step * index, index == count - 1 ? max : min + step * (index + 1)))
      .ToList();
  }

  // Green (low) to red (high), like Civil 3D's default analysis schemes.
  private static Color RampColor(int index, int count)
  {
    var t = count <= 1 ? 0d : index / (double)(count - 1);
    var red = (byte)Math.Round(255 * Math.Min(1d, 2d * t));
    var green = (byte)Math.Round(255 * Math.Min(1d, 2d * (1d - t)));
    return Color.FromRgb(red, green, 0);
  }

  private static void ApplyLabelStyle(Transaction transaction, IEnumerable<ObjectId> labelIds, ObjectId styleId)
  {
    if (styleId == ObjectId.Null)
    {
      return;
    }

    foreach (var labelId in labelIds)
    {
      var label = CivilObjectUtils.GetRequiredObject<Label>(transaction, labelId, OpenMode.ForWrite);
      label.StyleId = styleId;
    }
  }

  private static List<ObjectId> ContourLabelGroupIds(Database database, Transaction transaction, ObjectId surfaceId)
  {
    var ids = new List<ObjectId>();
    var modelSpace = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(
      transaction,
      SymbolUtilityServices.GetBlockModelSpaceId(database),
      OpenMode.ForRead);
    foreach (ObjectId id in modelSpace)
    {
      if (id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(SurfaceContourLabelGroup))) &&
          transaction.GetObject(id, OpenMode.ForRead) is SurfaceContourLabelGroup group &&
          group.FeatureId == surfaceId)
      {
        ids.Add(id);
      }
    }

    return ids;
  }

  private static int CountContourLabelGroups(Database database, Transaction transaction, ObjectId surfaceId) =>
    ContourLabelGroupIds(database, transaction, surfaceId).Count;

  private static Point2d ParseRequiredPoint2d(JsonObject? parameters, string name)
  {
    if (PluginRuntime.GetParameter(parameters, name) is not JsonObject point || point["x"] == null || point["y"] == null)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"'{name}' with x and y is required.");
    }

    return new Point2d(point["x"]!.GetValue<double>(), point["y"]!.GetValue<double>());
  }
}
