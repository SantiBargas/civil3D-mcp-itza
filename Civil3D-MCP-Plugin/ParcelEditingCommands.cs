using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpPlugin;

/// <summary>
/// Handlers for civil3d_parcel_* editing tools.
///
/// Civil 3D API notes:
///   Parcel/site collection members vary by Civil 3D version, so this implementation
///   relies on reflection for site/parcel enumeration and creation fallbacks.
/// </summary>
public static class ParcelEditingCommands
{
  // -------------------------------------------------------------------------
  // listParcelSites
  // -------------------------------------------------------------------------

  public static Task<object?> ListParcelSitesAsync()
  {
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var rows = new List<Dictionary<string, object?>>();
      foreach (ObjectId sid in EnumerateSiteIds(civilDoc))
      {
        var site = CivilObjectUtils.GetRequiredObject<Site>(transaction, sid, OpenMode.ForRead);
        rows.Add(new Dictionary<string, object?>
        {
          ["name"] = site.Name,
          ["handle"] = CivilObjectUtils.GetHandle(site),
          ["parcelCount"] = EnumerateParcelIds(site, transaction).Count(),
        });
      }

      return new Dictionary<string, object?>
      {
        ["sites"] = rows,
      };
    });
  }

  // -------------------------------------------------------------------------
  // createSite
  // -------------------------------------------------------------------------

  public static Task<object?> CreateSiteAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      foreach (ObjectId sid in EnumerateSiteIds(civilDoc))
      {
        var existing = CivilObjectUtils.GetRequiredObject<Site>(transaction, sid, OpenMode.ForRead);
        if (string.Equals(existing.Name, siteName, StringComparison.OrdinalIgnoreCase))
          throw new JsonRpcDispatchException("CIVIL3D.CONFLICT", $"Site '{siteName}' already exists.");
      }

      var siteId = Site.Create(civilDoc, siteName);
      var site = CivilObjectUtils.GetRequiredObject<Site>(transaction, siteId, OpenMode.ForRead);

      return new Dictionary<string, object?>
      {
        ["name"] = site.Name,
        ["handle"] = CivilObjectUtils.GetHandle(site),
        ["created"] = true,
      };
    });
  }

  // -------------------------------------------------------------------------
  // listParcels
  // -------------------------------------------------------------------------

  public static Task<object?> ListParcelsAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);
      var parcels = new List<Dictionary<string, object?>>();

      foreach (ObjectId pid in EnumerateParcelIds(site, transaction))
      {
        var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, pid, OpenMode.ForRead);
        parcels.Add(new Dictionary<string, object?>
        {
          ["name"] = parcel.Name,
          ["handle"] = CivilObjectUtils.GetHandle(parcel),
          ["number"] = GetParcelNumber(parcel),
          ["area"] = parcel.Area,
          ["perimeter"] = GetParcelPerimeter(parcel),
          ["style"] = GetParcelStyleName(parcel, transaction),
        });
      }

      return new Dictionary<string, object?>
      {
        ["siteName"] = siteName,
        ["parcels"] = parcels,
        ["units"] = new Dictionary<string, object?>
        {
          ["area"] = $"{CivilObjectUtils.LinearUnits(database)}²",
          ["length"] = CivilObjectUtils.LinearUnits(database),
        },
      };
    });
  }

  // -------------------------------------------------------------------------
  // getParcel
  // -------------------------------------------------------------------------

  public static Task<object?> GetParcelAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    var parcelName = PluginRuntime.GetRequiredString(parameters, "parcelName");

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);
      var parcel = FindParcelByName(site, transaction, parcelName, OpenMode.ForRead);

      return new Dictionary<string, object?>
      {
        ["siteName"] = siteName,
        ["name"] = parcel.Name,
        ["handle"] = CivilObjectUtils.GetHandle(parcel),
        ["number"] = GetParcelNumber(parcel),
        ["area"] = parcel.Area,
        ["perimeter"] = GetParcelPerimeter(parcel),
        ["style"] = GetParcelStyleName(parcel, transaction),
      };
    });
  }

  // -------------------------------------------------------------------------
  // createParcel
  // -------------------------------------------------------------------------

  public static Task<object?> CreateParcelAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    var parcelName = PluginRuntime.GetOptionalString(parameters, "name");
    var sourceHandle = PluginRuntime.GetOptionalString(parameters, "sourceHandle");
    var style = PluginRuntime.GetOptionalString(parameters, "style");
    var areaLabelStyle = PluginRuntime.GetOptionalString(parameters, "areaLabelStyle");
    var pointsNode = PluginRuntime.GetParameter(parameters, "points") as JsonArray;

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);

      ObjectId newParcelId = ObjectId.Null;

      if (!string.IsNullOrWhiteSpace(sourceHandle))
      {
        // Create from existing entity handle
        var handle = new Handle(Convert.ToInt64(sourceHandle, 16));
        if (!database.TryGetObjectId(handle, out var entityId) || entityId.IsNull)
          throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND",
            $"Entity with handle '{sourceHandle}' was not found.");

        newParcelId = CreateParcelFromEntity(site, entityId, transaction);
      }
      else if (pointsNode != null && pointsNode.Count >= 3)
      {
        // Create from vertex list
        newParcelId = CreateParcelFromPoints(site, pointsNode, database, transaction);
      }
      else
      {
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          "createParcel requires either a sourceHandle (entity handle) or a points array with ≥3 vertices.");
      }

      if (newParcelId.IsNull)
        throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", "Parcel was not created. Check that the boundary is closed and forms a valid area.");

      var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, newParcelId, OpenMode.ForWrite);

      // Apply optional properties
      if (!string.IsNullOrWhiteSpace(parcelName)) CivilObjectUtils.TrySetName(parcel, parcelName);
      if (!string.IsNullOrWhiteSpace(style)) ApplyStyleToParcel(parcel, style, civilDoc, transaction);
      if (!string.IsNullOrWhiteSpace(areaLabelStyle)) ApplyLabelStyleToParcel(parcel, areaLabelStyle, civilDoc, transaction);

      return new Dictionary<string, object?>
      {
        ["siteName"] = siteName,
        ["name"] = parcel.Name,
        ["handle"] = CivilObjectUtils.GetHandle(parcel),
        ["area"] = parcel.Area,
        ["perimeter"] = GetParcelPerimeter(parcel),
        ["created"] = true,
      };
    });
  }

  // -------------------------------------------------------------------------
  // createParcels (batch)
  // -------------------------------------------------------------------------

  private const int MaxBatchParcels = 300;

  // Everything is validated before the first parcel is created, so input errors never leave a
  // partial batch. A Civil 3D failure mid-batch throws and the transaction is not committed.
  public static Task<object?> CreateParcelsAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    if (PluginRuntime.GetParameter(parameters, "parcels") is not JsonArray parcelsNode || parcelsNode.Count == 0)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "createParcels requires a non-empty 'parcels' array.");
    if (parcelsNode.Count > MaxBatchParcels)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
        $"createParcels accepts at most {MaxBatchParcels} parcels per call (got {parcelsNode.Count}).");

    var items = new List<(string? Name, JsonArray Points, string? Style, string? LabelStyle)>();
    var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < parcelsNode.Count; index++)
    {
      if (parcelsNode[index] is not JsonObject item)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"parcels[{index}] must be an object.");

      var name = PluginRuntime.GetOptionalString(item, "name");
      var label = string.IsNullOrWhiteSpace(name) ? $"parcels[{index}]" : $"parcels[{index}] '{name}'";
      if (PluginRuntime.GetParameter(item, "points") is not JsonArray points || points.Count < 3)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"{label} needs a 'points' array with ≥3 vertices.");
      if (Math.Abs(SignedArea(points)) < 1e-9)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"{label} has zero area.");
      if (!string.IsNullOrWhiteSpace(name) && !batchNames.Add(name))
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"{label} is duplicated within the batch.");

      items.Add((name, points, PluginRuntime.GetOptionalString(item, "style"),
        PluginRuntime.GetOptionalString(item, "areaLabelStyle")));
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);

      foreach (var parcelId in EnumerateParcelIds(site, transaction))
      {
        var existing = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, parcelId, OpenMode.ForRead);
        if (batchNames.Contains(existing.Name))
          throw new JsonRpcDispatchException("CIVIL3D.CONFLICT",
            $"Parcel '{existing.Name}' already exists in site '{site.Name}'.");
      }

      // Civil 3D forms one parcel per closed region of the site's segment network, not one per
      // polygon: a polygon may close no new region (earlier neighbours already closed it) or several
      // at once. Parcels formed in this batch wait in `unclaimed` until the item whose polygon they
      // match claims them.
      var unclaimed = new List<ObjectId>();
      var created = new List<Dictionary<string, object?>>();
      var totalArea = 0.0;
      for (var index = 0; index < items.Count; index++)
      {
        var (name, points, style, labelStyle) = items[index];
        var polygon = ParsePolygon(points);
        unclaimed.AddRange(AddPolygonSegments(site, polygon, database, transaction));
        var live = site.GetParcelIds().Cast<ObjectId>().ToHashSet();
        unclaimed.RemoveAll(id => !live.Contains(id));

        var newParcelId = MatchParcelToPolygon(transaction, unclaimed, polygon);
        if (newParcelId.IsNull)
          throw new JsonRpcDispatchException("CIVIL3D.API_ERROR",
            $"parcels[{index}]{(string.IsNullOrWhiteSpace(name) ? "" : $" '{name}'")} did not form a parcel matching its polygon " +
            $"(area {Math.Abs(PolygonArea(polygon)):F3}; {created.Count} created before it; the batch is not committed). " +
            "Check that the polygon is closed, does not overlap parcels already in the site, and that its edges meet its neighbours' vertices. " +
            $"Unclaimed parcels formed in this batch: {DescribeParcels(transaction, unclaimed)}.");
        unclaimed.Remove(newParcelId);

        var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, newParcelId, OpenMode.ForWrite);
        if (!string.IsNullOrWhiteSpace(name)) CivilObjectUtils.TrySetName(parcel, name);
        if (!string.IsNullOrWhiteSpace(style)) ApplyStyleToParcel(parcel, style, civilDoc, transaction);
        if (!string.IsNullOrWhiteSpace(labelStyle)) ApplyLabelStyleToParcel(parcel, labelStyle, civilDoc, transaction);

        totalArea += parcel.Area;
        created.Add(new Dictionary<string, object?>
        {
          ["name"] = parcel.Name,
          ["handle"] = CivilObjectUtils.GetHandle(parcel),
          ["area"] = parcel.Area,
          ["perimeter"] = GetParcelPerimeter(parcel),
        });
      }

      var current = site.GetParcelIds().Cast<ObjectId>().ToHashSet();
      var unassigned = unclaimed.Where(current.Contains).Select(id =>
      {
        var leftover = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, id, OpenMode.ForRead);
        return new Dictionary<string, object?>
        {
          ["name"] = leftover.Name,
          ["handle"] = CivilObjectUtils.GetHandle(leftover),
          ["area"] = leftover.Area,
        };
      }).ToList();

      return new Dictionary<string, object?>
      {
        ["siteName"] = siteName,
        ["createdCount"] = created.Count,
        ["totalArea"] = totalArea,
        ["parcels"] = created,
        ["unassignedParcels"] = unassigned,
      };
    });
  }

  private static double SignedArea(JsonArray points)
  {
    var sum = 0.0;
    for (var i = 0; i < points.Count; i++)
    {
      if (points[i] is not JsonArray a || a.Count < 2 || points[(i + 1) % points.Count] is not JsonArray b || b.Count < 2)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Every vertex must be an [x, y] pair.");
      sum += a[0]!.GetValue<double>() * b[1]!.GetValue<double>() - b[0]!.GetValue<double>() * a[1]!.GetValue<double>();
    }
    return sum / 2;
  }

  // -------------------------------------------------------------------------
  // editParcel
  // -------------------------------------------------------------------------

  public static Task<object?> EditParcelAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    var parcelName = PluginRuntime.GetRequiredString(parameters, "parcelName");
    var newName = PluginRuntime.GetOptionalString(parameters, "newName");
    var style = PluginRuntime.GetOptionalString(parameters, "style");
    var areaLabelStyle = PluginRuntime.GetOptionalString(parameters, "areaLabelStyle");
    var description = PluginRuntime.GetOptionalString(parameters, "description");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);
      var parcel = FindParcelByName(site, transaction, parcelName, OpenMode.ForWrite);

      if (!string.IsNullOrWhiteSpace(newName)) CivilObjectUtils.TrySetName(parcel, newName);
      if (!string.IsNullOrWhiteSpace(style)) ApplyStyleToParcel(parcel, style, civilDoc, transaction);
      if (!string.IsNullOrWhiteSpace(areaLabelStyle)) ApplyLabelStyleToParcel(parcel, areaLabelStyle, civilDoc, transaction);
      if (!string.IsNullOrWhiteSpace(description))
      {
        parcel.Description = description;
      }

      return new Dictionary<string, object?>
      {
        ["siteName"] = siteName,
        ["name"] = parcel.Name,
        ["handle"] = CivilObjectUtils.GetHandle(parcel),
        ["area"] = parcel.Area,
        ["updated"] = true,
      };
    });
  }

  // -------------------------------------------------------------------------
  // adjustParcelLotLine
  // -------------------------------------------------------------------------

  public static Task<object?> AdjustParcelLotLineAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    var parcelName = PluginRuntime.GetRequiredString(parameters, "parcelName");
    var targetAreaSqFt = PluginRuntime.GetRequiredDouble(parameters, "targetAreaSqFt");
    var lotLineHandle = PluginRuntime.GetOptionalString(parameters, "lotLineHandle");
    var tolerance = PluginRuntime.GetOptionalDouble(parameters, "tolerance") ?? 1.0;

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);
      var parcel = FindParcelByName(site, transaction, parcelName, OpenMode.ForRead);

      // Neither method exists in Civil 3D 2026 (.NET or COM — checked in AeccDbMgd.dll and
      // Autodesk.AECC.Interop.Land.dll); kept in case a future release adds one, but the action
      // must not report "attempted" when nothing ran.
      var targetArea = TargetAreaToDrawingUnits(targetAreaSqFt, database);
      var invoked = Civil3DCompatibility.TryInvokeMethod(parcel, "AdjustLotLine", out _,
          targetArea, tolerance, lotLineHandle)
        || Civil3DCompatibility.TryInvokeMethod(parcel, "SlideAngle", out _, targetArea, tolerance);
      if (!invoked)
        throw new JsonRpcDispatchException("CIVIL3D.NOT_SUPPORTED",
          "lot_line_adjust is not available: the Civil 3D API exposes no lot-line slide/swing method. " +
          "Recompute the lot geometry and recreate the parcels instead.");

      var actualArea = parcel.Area;
      var converged = Math.Abs(actualArea - targetArea) < tolerance;

      return new Dictionary<string, object?>
      {
        ["siteName"] = siteName,
        ["parcelName"] = parcelName,
        ["targetAreaSqFt"] = targetAreaSqFt,
        ["actualArea"] = actualArea,
        ["converged"] = converged,
        ["message"] = converged
          ? $"Lot line adjusted. Parcel area is now {actualArea:F2} drawing units²."
          : "Lot line adjustment attempted. Verify the result in Civil 3D.",
      };
    });
  }

  // -------------------------------------------------------------------------
  // reportParcels
  // -------------------------------------------------------------------------

  public static Task<object?> ReportParcelsAsync(JsonObject? parameters)
  {
    var siteName = PluginRuntime.GetRequiredString(parameters, "siteName");
    var parcelNamesNode = PluginRuntime.GetParameter(parameters, "parcelNames") as JsonArray;
    var outputPath = PluginRuntime.GetOptionalString(parameters, "outputPath");
    var includeCoordinates = PluginRuntime.GetOptionalBool(parameters, "includeCoordinates") ?? false;
    var requestedUnits = PluginRuntime.GetOptionalString(parameters, "units");
    var overwrite = PluginRuntime.GetOptionalBool(parameters, "overwrite") ?? false;

    var filterNames = parcelNamesNode?
      .Select(n => n?.GetValue<string>())
      .Where(n => !string.IsNullOrWhiteSpace(n))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);
      var rows = new List<Dictionary<string, object?>>();
      var squareMetersPerUnit = SquareMetersPerDrawingUnit(database);
      var units = requestedUnits
        ?? (squareMetersPerUnit == null ? "drawing units²" : CivilObjectUtils.LinearUnits(database) == "feet" ? "sqft" : "sqm");
      if (requestedUnits != null && squareMetersPerUnit == null)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"Drawing units ({database.Insunits}) can't be converted to '{requestedUnits}'; omit 'units' to get the area in drawing units.");

      foreach (ObjectId pid in EnumerateParcelIds(site, transaction))
      {
        var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, pid, OpenMode.ForRead);
        if (filterNames != null && filterNames.Count > 0 && !filterNames.Contains(parcel.Name)) continue;

        var areaInUnits = squareMetersPerUnit is { } factor ? ConvertArea(parcel.Area * factor, units) : parcel.Area;

        var row = new Dictionary<string, object?>
        {
          ["name"] = parcel.Name,
          ["handle"] = CivilObjectUtils.GetHandle(parcel),
          ["area"] = areaInUnits,
          ["areaUnits"] = units,
          ["perimeter"] = GetParcelPerimeter(parcel),
          ["style"] = GetParcelStyleName(parcel, transaction),
        };

        if (includeCoordinates)
        {
          row["vertices"] = GetParcelVertices(parcel);
        }

        rows.Add(row);
      }

      if (!string.IsNullOrWhiteSpace(outputPath))
      {
        outputPath = WriteCsv(outputPath, rows, includeCoordinates, overwrite);
      }

      return new Dictionary<string, object?>
      {
        ["siteName"] = siteName,
        ["parcelCount"] = rows.Count,
        ["parcels"] = rows,
        ["outputPath"] = outputPath,
      };
    });
  }

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

  private static Site FindSiteByName(
    Autodesk.Civil.ApplicationServices.CivilDocument civilDoc,
    Transaction transaction, string siteName)
  {
    foreach (ObjectId sid in EnumerateSiteIds(civilDoc))
    {
      var site = CivilObjectUtils.GetRequiredObject<Site>(transaction, sid, OpenMode.ForRead);
      if (string.Equals(site.Name, siteName, StringComparison.OrdinalIgnoreCase)) return site;
    }
    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Site '{siteName}' was not found.");
  }

  private static Parcel FindParcelByName(Site site, Transaction transaction, string parcelName, OpenMode mode)
  {
    foreach (ObjectId pid in EnumerateParcelIds(site, transaction))
    {
      var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, pid, mode);
      if (string.Equals(parcel.Name, parcelName, StringComparison.OrdinalIgnoreCase)) return parcel;
    }
    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Parcel '{parcelName}' was not found in site '{site.Name}'.");
  }

  // Civil 3D's .NET API exposes no parcel-creation method (Site only has GetParcelIds and
  // ParcelSegment has no static creators), so parcels are created through COM:
  // AeccSite.ParcelSegments.AddFromEntity, after which Civil 3D forms the parcel itself.
  private static ObjectId CreateParcelFromEntity(Site site, ObjectId entityId, Transaction transaction)
  {
    var before = site.GetParcelIds().Cast<ObjectId>().ToHashSet();
    var entity = transaction.GetObject(entityId, OpenMode.ForRead);

    try
    {
      dynamic comSite = site.AcadObject;
      comSite.ParcelSegments.AddFromEntity(entity.AcadObject, false);
    }
    catch (Exception ex)
    {
      throw new JsonRpcDispatchException("CIVIL3D.API_ERROR",
        $"ParcelSegments.AddFromEntity failed: {ex.Message}");
    }

    return site.GetParcelIds().Cast<ObjectId>().FirstOrDefault(id => !before.Contains(id));
  }

  private static ObjectId CreateParcelFromPoints(Site site, JsonArray pointsNode,
    Database database, Transaction transaction)
  {
    var polygon = ParsePolygon(pointsNode);
    var newIds = AddPolygonSegments(site, polygon, database, transaction);
    return MatchParcelToPolygon(transaction, newIds, polygon);
  }

  private static List<Point2d> ParsePolygon(JsonArray pointsNode)
  {
    var pts = new List<Point2d>();
    foreach (var node in pointsNode)
    {
      if (node is JsonArray arr && arr.Count >= 2)
        pts.Add(new Point2d(arr[0]?.GetValue<double>() ?? 0, arr[1]?.GetValue<double>() ?? 0));
    }
    if (pts.Count > 1 && pts[0] == pts[^1])
      pts.RemoveAt(pts.Count - 1);
    return pts;
  }

  // Adds the polygon's edges to the site through a temporary closed polyline and returns the
  // parcels Civil 3D formed as a result (zero, one or several).
  private static List<ObjectId> AddPolygonSegments(Site site, List<Point2d> polygon,
    Database database, Transaction transaction)
  {
    var pline = new Polyline();
    for (var index = 0; index < polygon.Count; index++)
    {
      pline.AddVertexAt(index, polygon[index], 0, 0, 0);
    }

    pline.Closed = true;

    var modelSpaceId = CivilObjectUtils.GetModelSpaceBlockId(database, transaction);
    var btr = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(transaction, modelSpaceId, OpenMode.ForWrite);
    var tmpId = btr.AppendEntity(pline);
    transaction.AddNewlyCreatedDBObject(pline, true);

    var before = site.GetParcelIds().Cast<ObjectId>().ToHashSet();
    try
    {
      dynamic comSite = site.AcadObject;
      comSite.ParcelSegments.AddFromEntity(pline.AcadObject, false);
    }
    catch (Exception ex)
    {
      throw new JsonRpcDispatchException("CIVIL3D.API_ERROR",
        $"ParcelSegments.AddFromEntity failed: {ex.Message}");
    }

    pline.Erase(true);
    return site.GetParcelIds().Cast<ObjectId>().Where(id => !before.Contains(id)).ToList();
  }

  // Picks the candidate whose area and bounding box equal the polygon's. Parcel.Centroid can't be
  // used: it reads (0, 0) for parcels formed inside the still-open transaction. Falls back to a
  // unique area match when the extents are unavailable.
  private static ObjectId MatchParcelToPolygon(Transaction transaction, IEnumerable<ObjectId> candidates,
    List<Point2d> polygon)
  {
    var target = Math.Abs(PolygonArea(polygon));
    var areaTolerance = Math.Max(1e-3, target * 1e-6);
    var (minX, minY, maxX, maxY) = (polygon.Min(p => p.X), polygon.Min(p => p.Y), polygon.Max(p => p.X), polygon.Max(p => p.Y));
    var extentsTolerance = Math.Max(1e-3, Math.Max(maxX - minX, maxY - minY) * 1e-6);
    var areaMatches = new List<ObjectId>();
    foreach (var id in candidates)
    {
      var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, id, OpenMode.ForRead);
      if (Math.Abs(parcel.Area - target) > areaTolerance) continue;
      var extents = TryGetExtents(parcel);
      if (extents is { } e
          && Math.Abs(e.MinPoint.X - minX) <= extentsTolerance && Math.Abs(e.MinPoint.Y - minY) <= extentsTolerance
          && Math.Abs(e.MaxPoint.X - maxX) <= extentsTolerance && Math.Abs(e.MaxPoint.Y - maxY) <= extentsTolerance)
        return id;
      areaMatches.Add(id);
    }
    return areaMatches.Count == 1 ? areaMatches[0] : ObjectId.Null;
  }

  private static Extents3d? TryGetExtents(Autodesk.AutoCAD.DatabaseServices.Entity entity)
  {
    try
    {
      return entity.GeometricExtents;
    }
    catch (Autodesk.AutoCAD.Runtime.Exception)
    {
      return null;
    }
  }

  private static string DescribeParcels(Transaction transaction, IEnumerable<ObjectId> ids)
  {
    var parts = ids.Select(id =>
    {
      var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, id, OpenMode.ForRead);
      var extents = TryGetExtents(parcel) is { } e
        ? FormattableString.Invariant($"({e.MinPoint.X:F3}, {e.MinPoint.Y:F3})-({e.MaxPoint.X:F3}, {e.MaxPoint.Y:F3})")
        : "n/a";
      return FormattableString.Invariant($"{CivilObjectUtils.GetHandle(parcel)} area={parcel.Area:F4} extents={extents}");
    }).ToList();
    return parts.Count == 0 ? "none" : string.Join("; ", parts);
  }

  private static double PolygonArea(List<Point2d> polygon)
  {
    var sum = 0.0;
    for (var i = 0; i < polygon.Count; i++)
    {
      var a = polygon[i];
      var b = polygon[(i + 1) % polygon.Count];
      sum += a.X * b.Y - b.X * a.Y;
    }
    return sum / 2;
  }

  private static void ApplyStyleToParcel(Parcel parcel, string styleName,
    Autodesk.Civil.ApplicationServices.CivilDocument civilDoc, Transaction transaction)
  {
    parcel.StyleId = LookupUtils.GetParcelStyleId(civilDoc, transaction, styleName);
  }

  private static void ApplyLabelStyleToParcel(Parcel parcel, string labelStyleName,
    Autodesk.Civil.ApplicationServices.CivilDocument civilDoc, Transaction transaction)
  {
    parcel.AreaSelectionLabelStyleId = LookupUtils.GetParcelAreaLabelStyleId(civilDoc, transaction, labelStyleName);
  }

  private static IEnumerable<ObjectId> EnumerateSiteIds(Autodesk.Civil.ApplicationServices.CivilDocument civilDoc)
  {
    foreach (ObjectId objectId in civilDoc.GetSiteIds())
    {
      if (objectId != ObjectId.Null)
        yield return objectId;
    }
  }

  private static IEnumerable<ObjectId> EnumerateParcelIds(Site site, Transaction transaction)
  {
    var documentedIds = new HashSet<ObjectId>();
    foreach (ObjectId objectId in site.GetParcelIds())
    {
      if (objectId != ObjectId.Null && documentedIds.Add(objectId))
        yield return objectId;
    }

    // Compatibility fallbacks are retained for older host variations, but
    // de-duplicate anything already returned by the documented 2026 API.
    foreach (var memberName in new[] { "Parcels", "ParcelCollection", "ParcelIds" })
    {
      var value = GetNamedMember(site, memberName) ?? CivilObjectUtils.InvokeMethod(site, memberName);

      foreach (var objectId in CivilObjectUtils.ToObjectIds(value))
      {
        if (objectId != ObjectId.Null && documentedIds.Add(objectId))
          yield return objectId;
      }

      if (value is System.Collections.IEnumerable enumerable)
      {
        foreach (var item in enumerable)
        {
          if (item is Parcel parcel)
          {
            if (documentedIds.Add(parcel.ObjectId)) yield return parcel.ObjectId;
          }
          else if (item is Autodesk.AutoCAD.DatabaseServices.DBObject dbObject)
          {
            if (documentedIds.Add(dbObject.ObjectId)) yield return dbObject.ObjectId;
          }
        }
      }
    }
  }

  private static object? GetNamedMember(object? value, string memberName)
  {
    if (value == null) return null;

    return Civil3DCompatibility.GetPropertyValue(value, memberName)
      ?? Civil3DCompatibility.GetFieldValue(value, memberName);
  }

  // Parcel has no Perimeter in the .NET API; COM's AeccParcel.Statistics does.
  private static double? GetParcelPerimeter(Parcel parcel)
  {
    try
    {
      dynamic comParcel = parcel.AcadObject;
      return (double)comParcel.Statistics.Perimeter;
    }
    catch
    {
      return null;
    }
  }

  private static string? GetParcelStyleName(Parcel parcel, Transaction transaction)
  {
    if (parcel.StyleId.IsNull) return null;
    return transaction.GetObject(parcel.StyleId, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Styles.StyleBase style
      ? style.Name
      : null;
  }

  private static int GetParcelNumber(Parcel parcel)
  {
    return parcel.Number;
  }

  private static double ConvertArea(double squareMeters, string units)
  {
    return units.ToLowerInvariant() switch
    {
      "sqm" => squareMeters,
      "ha" => squareMeters / 10000.0,
      "sqft" => squareMeters / 0.09290304,
      "acres" => squareMeters / 4046.8564224,
      _ => throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Unknown area units '{units}'."),
    };
  }

  private static double? SquareMetersPerDrawingUnit(Database database)
  {
    var insunits = database.Insunits;
    if (insunits == UnitsValue.Meters) return 1.0;
    if (insunits == UnitsValue.Feet) return 0.09290304;
    if (insunits.ToString().Contains("Survey", StringComparison.OrdinalIgnoreCase)
        && insunits.ToString().Contains("Feet", StringComparison.OrdinalIgnoreCase))
      return Math.Pow(1200.0 / 3937.0, 2);
    if (insunits == UnitsValue.Millimeters) return 1e-6;
    if (insunits == UnitsValue.Centimeters) return 1e-4;
    if (insunits == UnitsValue.Kilometers) return 1e6;
    if (insunits == UnitsValue.Inches) return 0.00064516;
    return null;
  }

  // The .NET Parcel exposes no boundary (verified in AeccDbMgd.dll metadata); COM does:
  // AeccParcel.ParcelLoops[i][j] are segment elements with StartX/StartY/EndX/EndY. Arcs are
  // reported by their start point only.
  private static List<Dictionary<string, object?>> GetParcelVertices(Parcel parcel)
  {
    var vertices = new List<Dictionary<string, object?>>();
    try
    {
      dynamic comParcel = parcel.AcadObject;
      dynamic loops = comParcel.ParcelLoops;
      int loopCount = loops.Count;
      for (var loopIndex = 0; loopIndex < loopCount; loopIndex++)
      {
        dynamic loop = loops.Item(loopIndex);
        int elementCount = loop.Count;
        for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
        {
          dynamic element = loop.Item(elementIndex);
          vertices.Add(new Dictionary<string, object?>
          {
            ["loop"] = loopIndex,
            ["x"] = (double)element.StartX,
            ["y"] = (double)element.StartY,
          });
        }
      }
    }
    catch (Exception ex)
    {
      throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"Could not read the boundary of parcel '{parcel.Name}': {ex.Message}");
    }
    return vertices;
  }

  private static double TargetAreaToDrawingUnits(double sqFt, Database database)
  {
    return SquareMetersPerDrawingUnit(database) is { } factor ? sqFt * 0.09290304 / factor : sqFt;
  }

  private static string WriteCsv(
    string outputPath,
    List<Dictionary<string, object?>> rows,
    bool includeCoordinates,
    bool overwrite)
  {
    var csv = new System.Text.StringBuilder();
    csv.AppendLine(includeCoordinates ? "Name,Area,AreaUnits,Perimeter,Style,Vertices" : "Name,Area,AreaUnits,Perimeter,Style");
    foreach (var row in rows)
    {
      var fields = new List<string?>
      {
        row.GetValueOrDefault("name") as string,
        FormatCsvNumber(row.GetValueOrDefault("area")),
        row.GetValueOrDefault("areaUnits") as string,
        FormatCsvNumber(row.GetValueOrDefault("perimeter")),
        row.GetValueOrDefault("style") as string,
      };
      if (includeCoordinates && row.GetValueOrDefault("vertices") is List<Dictionary<string, object?>> vertices)
      {
        fields.Add(string.Join(";", vertices.Select(v =>
          $"{FormatCsvNumber(v.GetValueOrDefault("x"))} {FormatCsvNumber(v.GetValueOrDefault("y"))}")));
      }
      csv.AppendLine(string.Join(",", fields.Select(EscapeCsvField)));
    }

    return FileBoundary.WriteAllTextAtomic(
      outputPath, csv.ToString(), System.Text.Encoding.UTF8, overwrite, ".csv");
  }

  private static string? FormatCsvNumber(object? value)
  {
    return value is double number ? number.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) : value?.ToString();
  }

  private static string EscapeCsvField(string? value)
  {
    if (string.IsNullOrEmpty(value)) return "";
    return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
  }
}
