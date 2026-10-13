using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace Civil3DMcpPlugin;

/// <summary>
/// Batch labeling of parcels, parcel segments, pipes and structures with the typed Civil 3D API
/// (ParcelAreaLabel / ParcelSegmentLabel / PipeLabel / StructureLabel.Create, verified in the
/// AeccDbMgd 2026 metadata). One approval and one transaction per batch.
/// </summary>
public static class LabelBatchCommands
{
  private const int MaxItemsInResponse = 50;

  public static LabelStyleCollection? GetTypedLabelStyles(CivilDocument civilDoc, string kind)
  {
    var labelStyles = civilDoc.Styles.LabelStyles;
    return kind.ToLowerInvariant() switch
    {
      "parcel_area" => labelStyles.ParcelLabelStyles.AreaLabelStyles,
      "parcel_line" or "parcel_segment" => labelStyles.ParcelLabelStyles.LineLabelStyles,
      "parcel_curve" => labelStyles.ParcelLabelStyles.CurveLabelStyles,
      "pipe" => labelStyles.PipeLabelStyles.PlanProfileLabelStyles,
      "structure" => labelStyles.StructureLabelStyles.LabelStyles,
      _ => null,
    };
  }

  public static Task<object?> AddLabelsAsync(JsonObject? parameters)
  {
    var kind = PluginRuntime.GetRequiredString(parameters, "kind").ToLowerInvariant();
    var labelStyle = PluginRuntime.GetOptionalString(parameters, "labelStyle");
    var curveLabelStyle = PluginRuntime.GetOptionalString(parameters, "curveLabelStyle");
    var layerName = PluginRuntime.GetOptionalString(parameters, "layer");
    var names = (PluginRuntime.GetParameter(parameters, "names") as JsonArray)?
      .Select(node => node?.GetValue<string>())
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .Select(name => name!)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var filter = names is { Count: > 0 } ? names : null;
    var namePattern = PluginRuntime.GetOptionalString(parameters, "namePattern");
    Regex? patternRegex;
    try
    {
      patternRegex = string.IsNullOrWhiteSpace(namePattern) ? null : new Regex(namePattern, RegexOptions.IgnoreCase);
    }
    catch (ArgumentException exception)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"namePattern is not a valid regular expression: {exception.Message}");
    }
    bool Excluded(string name) => (filter != null && !filter.Contains(name)) || (patternRegex != null && !patternRegex.IsMatch(name));

    return CivilExecution.WriteAsync<object?>((doc, civilDocObject, database, transaction) =>
    {
      var civilDoc = (CivilDocument)civilDocObject;
      var layerId = string.IsNullOrWhiteSpace(layerName) ? ObjectId.Null : LookupUtils.GetLayerId(database, transaction, layerName);
      var items = new List<Dictionary<string, object?>>();
      var counts = new Dictionary<string, int> { ["created"] = 0, ["restyled"] = 0, ["skipped"] = 0, ["duplicates"] = 0 };
      var styleNames = new Dictionary<string, object?>();

      switch (kind)
      {
        case "parcel_area":
        {
          var site = FindSite(civilDoc, transaction, PluginRuntime.GetRequiredString(parameters, "siteName"));
          var (styleId, styleName) = ResolveStyle(civilDoc, transaction, "parcel_area", labelStyle);
          styleNames["labelStyle"] = styleName;
          foreach (ObjectId parcelId in site.GetParcelIds())
          {
            var parcel = CivilObjectUtils.GetRequiredObject<Parcel>(transaction, parcelId, OpenMode.ForRead);
            if (Excluded(parcel.Name)) continue;
            RunItem(items.Count, parcel.Name, () =>
            {
              // Parcels already carry an area label from the site defaults: restyle it instead of stacking a second one.
              var existing = parcel.GetAvailableParcelAreaLabelIds();
              if (existing.Count > 0)
              {
                foreach (ObjectId labelId in existing)
                {
                  var label = CivilObjectUtils.GetRequiredObject<Label>(transaction, labelId, OpenMode.ForWrite);
                  label.StyleId = styleId;
                  if (!layerId.IsNull) label.LayerId = layerId;
                  counts["restyled"]++;
                }
                items.Add(Item(parcel.Name, existing[0], transaction, "restyled"));
              }
              else
              {
                var labelId = ParcelAreaLabel.Create(parcelId, styleId);
                ApplyLayer(transaction, labelId, layerId);
                counts["created"]++;
                items.Add(Item(parcel.Name, labelId, transaction, "created"));
              }
            });
          }
          break;
        }
        case "parcel_segment":
        {
          var site = FindSite(civilDoc, transaction, PluginRuntime.GetRequiredString(parameters, "siteName"));
          var (lineStyleId, lineStyleName) = ResolveStyle(civilDoc, transaction, "parcel_line", labelStyle);
          var (curveStyleId, curveStyleName) = ResolveStyle(civilDoc, transaction, "parcel_curve", curveLabelStyle);
          styleNames["labelStyle"] = lineStyleName;
          styleNames["curveLabelStyle"] = curveStyleName;
          var minLength = PluginRuntime.GetOptionalDouble(parameters, "minLength") ?? 0.5;
          var skipLabeled = PluginRuntime.GetOptionalBool(parameters, "skipLabeled") ?? true;
          var position = PluginRuntime.GetOptionalDouble(parameters, "position") ?? 0.5;
          if (position < 0 || position > 1)
            throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "position must be between 0 and 1.");
          var positionMinLength = PluginRuntime.GetOptionalDouble(parameters, "positionMinLength") ?? 0.0;
          var labeledEdges = new HashSet<(double, double, double)>();

          dynamic lineStyleCom = CivilObjectUtils.GetRequiredObject<Autodesk.AutoCAD.DatabaseServices.DBObject>(transaction, lineStyleId, OpenMode.ForRead).AcadObject;
          dynamic curveStyleCom = CivilObjectUtils.GetRequiredObject<Autodesk.AutoCAD.DatabaseServices.DBObject>(transaction, curveStyleId, OpenMode.ForRead).AcadObject;

          // .NET exposes no way to enumerate parcel segments; COM does (AeccSite.ParcelSegments).
          dynamic comSegments = ((dynamic)site.AcadObject).ParcelSegments;
          int segmentCount = comSegments.Count;
          for (var i = 0; i < segmentCount; i++)
          {
            dynamic comSegment = comSegments.Item(i);
            string handle = comSegment.Handle;
            var segmentId = database.GetObjectId(false, new Handle(Convert.ToInt64(handle, 16)), 0);
            var segment = CivilObjectUtils.GetRequiredObject<ParcelSegment>(transaction, segmentId, OpenMode.ForRead);
            if (skipLabeled && segment.GetAvailableParcelSegmentLabelIds().Count > 0)
            {
              counts["skipped"]++;
              continue;
            }

            int elementCount = comSegment.Count;
            var lengths = new double[elementCount];
            var midpoints = new Point2d[elementCount];
            var anchors = new Point2d[elementCount];
            for (var j = 0; j < elementCount; j++)
            {
              dynamic element = comSegment.Item(j);
              lengths[j] = (double)element.Length;
              var start = new Point2d((double)element.StartX, (double)element.StartY);
              var end = new Point2d((double)element.EndX, (double)element.EndY);
              midpoints[j] = new Point2d((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
              var t = lengths[j] >= positionMinLength ? position : 0.5;
              anchors[j] = new Point2d(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            }
            for (var j = 0; j < elementCount; j++)
            {
              var length = lengths[j];
              if (length < minLength)
              {
                counts["skipped"]++;
                continue;
              }
              // Lots added one polygon each leave shared edges duplicated in two segments: label each edge once.
              var key = (Math.Round(midpoints[j].X, 2), Math.Round(midpoints[j].Y, 2), Math.Round(length, 2));
              if (!labeledEdges.Add(key))
              {
                counts["duplicates"]++;
                continue;
              }
              var label = $"segment {handle} element {j}";
              var midpoint = anchors[j];
              RunItem(items.Count, label, () =>
              {
                // ParcelSegmentLabel.Create(segment, ratio, ...) rejected every ratio but 0 ("range [0, 0]",
                // seen live), so the label is placed by point through COM, which picks the element under it.
                dynamic comLabel = comSegment.SegmentLabels.Add(lineStyleCom, curveStyleCom, new double[] { midpoint.X, midpoint.Y, 0.0 });
                string labelHandle = comLabel.Handle;
                var labelId = database.GetObjectId(false, new Handle(Convert.ToInt64(labelHandle, 16)), 0);
                ApplyLayer(transaction, labelId, layerId);
                counts["created"]++;
                var entry = Item(label, labelId, transaction, "created");
                entry["length"] = Math.Round(length, 3);
                entry["midX"] = Math.Round(midpoint.X, 3);
                entry["midY"] = Math.Round(midpoint.Y, 3);
                items.Add(entry);
              });
            }
          }
          break;
        }
        case "pipe":
        {
          var network = FindNetwork(civilDoc, transaction, PluginRuntime.GetRequiredString(parameters, "networkName"));
          var (styleId, styleName) = ResolveStyle(civilDoc, transaction, "pipe", labelStyle);
          styleNames["labelStyle"] = styleName;
          var ratio = PluginRuntime.GetOptionalDouble(parameters, "ratio") ?? 0.5;
          foreach (ObjectId pipeId in network.GetPipeIds())
          {
            var pipe = CivilObjectUtils.GetRequiredObject<Pipe>(transaction, pipeId, OpenMode.ForRead);
            if (Excluded(pipe.Name)) continue;
            RunItem(items.Count, pipe.Name, () =>
            {
              var labelId = PipeLabel.Create(pipeId, ratio, styleId);
              ApplyLayer(transaction, labelId, layerId);
              counts["created"]++;
              items.Add(Item(pipe.Name, labelId, transaction, "created"));
            });
          }
          break;
        }
        case "structure":
        {
          var network = FindNetwork(civilDoc, transaction, PluginRuntime.GetRequiredString(parameters, "networkName"));
          var (styleId, styleName) = ResolveStyle(civilDoc, transaction, "structure", labelStyle);
          styleNames["labelStyle"] = styleName;
          var offsetX = PluginRuntime.GetOptionalDouble(parameters, "offsetX") ?? 3.0;
          var offsetY = PluginRuntime.GetOptionalDouble(parameters, "offsetY") ?? 3.0;
          foreach (ObjectId structureId in network.GetStructureIds())
          {
            var structure = CivilObjectUtils.GetRequiredObject<Structure>(transaction, structureId, OpenMode.ForRead);
            if (Excluded(structure.Name)) continue;
            RunItem(items.Count, structure.Name, () =>
            {
              var location = new Point3d(structure.Position.X + offsetX, structure.Position.Y + offsetY, 0.0);
              var labelId = StructureLabel.Create(structureId, styleId, location);
              ApplyLayer(transaction, labelId, layerId);
              counts["created"]++;
              items.Add(Item(structure.Name, labelId, transaction, "created"));
            });
          }
          break;
        }
        default:
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Unsupported label kind '{kind}'. Use parcel_area, parcel_segment, pipe or structure.");
      }

      if (filter != null)
      {
        var found = items.Select(item => item["name"] as string).Where(name => name != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = filter.Where(name => !found.Contains(name)).ToList();
        if (missing.Count > 0)
        {
          throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Not found: {string.Join(", ", missing)}. Nothing was labeled.");
        }
      }

      var result = new Dictionary<string, object?>
      {
        ["kind"] = kind,
        ["layer"] = string.IsNullOrWhiteSpace(layerName) ? null : layerName,
        ["created"] = counts["created"],
        ["restyled"] = counts["restyled"],
        ["skipped"] = counts["skipped"],
        ["duplicates"] = counts["duplicates"],
        // Large batches (hundreds of parcel sides) overflowed the MCP response; the first 50 are enough to check.
        ["items"] = items.Take(MaxItemsInResponse).ToList(),
        ["itemsOmitted"] = Math.Max(0, items.Count - MaxItemsInResponse),
      };
      foreach (var pair in styleNames) result[pair.Key] = pair.Value;
      return result;
    });
  }

  public static Task<object?> DeleteLabelsAsync(JsonObject? parameters)
  {
    var handles = (PluginRuntime.GetParameter(parameters, "handles") as JsonArray)?
      .Select(node => node?.GetValue<string>())
      .Where(handle => !string.IsNullOrWhiteSpace(handle))
      .Select(handle => handle!)
      .ToList() ?? new List<string>();
    if (handles.Count == 0)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Provide at least one label handle.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      // Validate everything first so a bad handle never leaves a half-deleted batch.
      var labels = new List<Label>();
      for (var i = 0; i < handles.Count; i++)
      {
        var id = ObjectId.Null;
        if (!long.TryParse(handles[i], System.Globalization.NumberStyles.HexNumber, null, out var value) || !database.TryGetObjectId(new Handle(value), out id) || id.IsErased)
          throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"handles[{i}] '{handles[i]}' does not exist. Nothing was deleted.");
        var label = transaction.GetObject(id, OpenMode.ForWrite) as Label
          ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"handles[{i}] '{handles[i]}' is not a Civil 3D label. Nothing was deleted.");
        labels.Add(label);
      }
      foreach (var label in labels) label.Erase();

      return new Dictionary<string, object?>
      {
        ["deleted"] = labels.Count,
      };
    });
  }

  private static void RunItem(int index, string name, Action action)
  {
    try
    {
      action();
    }
    catch (JsonRpcDispatchException)
    {
      throw;
    }
    catch (Exception exception)
    {
      throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"items[{index}] '{name}' failed: {exception.Message} Nothing was labeled.");
    }
  }

  private static Dictionary<string, object?> Item(string name, ObjectId labelId, Transaction transaction, string status)
  {
    var label = CivilObjectUtils.GetRequiredObject<Label>(transaction, labelId, OpenMode.ForRead);
    return new Dictionary<string, object?>
    {
      ["name"] = name,
      ["status"] = status,
      ["handle"] = CivilObjectUtils.GetHandle(label),
      ["x"] = Math.Round(label.LabelLocation.X, 3),
      ["y"] = Math.Round(label.LabelLocation.Y, 3),
    };
  }

  private static void ApplyLayer(Transaction transaction, ObjectId labelId, ObjectId layerId)
  {
    if (layerId.IsNull) return;
    var label = CivilObjectUtils.GetRequiredObject<Autodesk.AutoCAD.DatabaseServices.Entity>(transaction, labelId, OpenMode.ForWrite);
    label.LayerId = layerId;
  }

  public static (ObjectId Id, string Name) ResolveStyle(CivilDocument civilDoc, Transaction transaction, string kind, string? styleName)
  {
    var collection = GetTypedLabelStyles(civilDoc, kind)
      ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"No label style collection for '{kind}'.");
    var available = new List<string>();
    var first = (Id: ObjectId.Null, Name: string.Empty);
    foreach (ObjectId styleId in collection)
    {
      var style = CivilObjectUtils.GetRequiredObject<LabelStyle>(transaction, styleId, OpenMode.ForRead);
      available.Add(style.Name);
      if (first.Id.IsNull) first = (styleId, style.Name);
      if (styleName != null && string.Equals(style.Name, styleName, StringComparison.OrdinalIgnoreCase)) return (styleId, style.Name);
    }

    if (styleName == null && !first.Id.IsNull) return first;
    throw new JsonRpcDispatchException(
      "CIVIL3D.OBJECT_NOT_FOUND",
      $"Label style '{styleName}' ({kind}) was not found. Available: {string.Join(", ", available)}.");
  }

  private static Site FindSite(CivilDocument civilDoc, Transaction transaction, string siteName)
  {
    foreach (ObjectId siteId in civilDoc.GetSiteIds())
    {
      var site = CivilObjectUtils.GetRequiredObject<Site>(transaction, siteId, OpenMode.ForRead);
      if (string.Equals(site.Name, siteName, StringComparison.OrdinalIgnoreCase)) return site;
    }
    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Site '{siteName}' was not found.");
  }

  private static Network FindNetwork(CivilDocument civilDoc, Transaction transaction, string networkName)
  {
    foreach (ObjectId networkId in civilDoc.GetPipeNetworkIds())
    {
      var network = CivilObjectUtils.GetRequiredObject<Network>(transaction, networkId, OpenMode.ForRead);
      if (string.Equals(network.Name, networkName, StringComparison.OrdinalIgnoreCase)) return network;
    }
    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Pipe network '{networkName}' was not found.");
  }
}
