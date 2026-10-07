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

      // Try Civil 3D lot line slide API
      var result = CivilObjectUtils.InvokeMethod(parcel, "AdjustLotLine",
        targetAreaSqFt, tolerance, lotLineHandle);

      if (result == null)
      {
        result = CivilObjectUtils.InvokeMethod(parcel, "SlideAngle",
          targetAreaSqFt, tolerance);
      }

      var actualArea = parcel.Area;
      var converged = Math.Abs(actualArea - TargetAreaToDrawingUnits(targetAreaSqFt, doc)) < tolerance;

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
    var units = PluginRuntime.GetOptionalString(parameters, "units") ?? "sqft";
    var overwrite = PluginRuntime.GetOptionalBool(parameters, "overwrite") ?? false;

    var filterNames = parcelNamesNode?
      .Select(n => n?.GetValue<string>())
      .Where(n => !string.IsNullOrWhiteSpace(n))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var site = FindSiteByName(civilDoc, transaction, siteName);
      var rows = new List<Dictionary<string, object?>>();

      foreach (ObjectId pid in EnumerateParcelIds(site, transaction))
      {
        var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, pid, OpenMode.ForRead);
        if (filterNames != null && filterNames.Count > 0 && !filterNames.Contains(parcel.Name)) continue;

        var areaInUnits = ConvertArea(parcel.Area, units);

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
          var vertices = new List<Dictionary<string, object?>>();
          var boundary = CivilObjectUtils.InvokeMethod(parcel, "GetBoundary")
            ?? CivilObjectUtils.InvokeMethod(parcel, "GetVertices");
          if (boundary is System.Collections.IEnumerable pts)
          {
            foreach (var pt in pts)
            {
              double x = CivilObjectUtils.GetDoubleProperty(pt, "X") ?? 0;
              double y = CivilObjectUtils.GetDoubleProperty(pt, "Y") ?? 0;
              vertices.Add(new Dictionary<string, object?> { ["x"] = x, ["y"] = y });
            }
          }
          row["vertices"] = vertices;
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
    var pts = new List<Point2d>();
    foreach (var node in pointsNode)
    {
      if (node is JsonArray arr && arr.Count >= 2)
        pts.Add(new Point2d(arr[0]?.GetValue<double>() ?? 0, arr[1]?.GetValue<double>() ?? 0));
    }
    // Close the polygon
    if (pts.Count > 0 && pts[0] != pts[^1])
      pts.Add(pts[0]);

    // Create a temporary polyline and convert to parcel
    var pline = new Polyline();
    for (var index = 0; index < pts.Count; index++)
    {
      pline.AddVertexAt(index, pts[index], 0, 0, 0);
    }

    pline.Closed = true;

    var modelSpaceId = CivilObjectUtils.GetModelSpaceBlockId(database, transaction);
    var btr = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(transaction, modelSpaceId, OpenMode.ForWrite);
    var tmpId = btr.AppendEntity(pline);
    transaction.AddNewlyCreatedDBObject(pline, true);

    var parcelId = CreateParcelFromEntity(site, tmpId, transaction);

    // Erase the temporary polyline if parcel was created from it
    if (!parcelId.IsNull)
      pline.Erase(true);

    return parcelId;
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

  private static double ConvertArea(double areaInDrawingUnits, string units)
  {
    return units.ToLowerInvariant() switch
    {
      "acres" => areaInDrawingUnits / 43560.0,
      "sqm" => areaInDrawingUnits * 0.092903,
      "ha" => areaInDrawingUnits * 0.092903 / 10000.0,
      _ => areaInDrawingUnits, // sqft or default
    };
  }

  private static double TargetAreaToDrawingUnits(double sqFt, Autodesk.AutoCAD.ApplicationServices.Document doc)
  {
    // Drawing units are usually sq ft already for US drawings
    return sqFt;
  }

  private static string WriteCsv(
    string outputPath,
    List<Dictionary<string, object?>> rows,
    bool includeCoordinates,
    bool overwrite)
  {
    var csv = new System.Text.StringBuilder();
    csv.AppendLine("Name,Area,AreaUnits,Perimeter,Style");
    foreach (var row in rows)
    {
      csv.AppendLine(string.Join(",",
        row.GetValueOrDefault("name"), row.GetValueOrDefault("area"),
        row.GetValueOrDefault("areaUnits"), row.GetValueOrDefault("perimeter"),
        row.GetValueOrDefault("style")));
    }

    return FileBoundary.WriteAllTextAtomic(
      outputPath, csv.ToString(), System.Text.Encoding.UTF8, overwrite, ".csv");
  }
}
