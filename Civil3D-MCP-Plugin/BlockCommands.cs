using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcpPlugin;

/// <summary>
/// Block definitions built from simple geometry (circles and lines around the origin) and batch
/// insertion of block references in model space.
/// </summary>
public static class BlockCommands
{
  public static Task<object?> DefineBlockAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "blockName");
    var circles = (PluginRuntime.GetParameter(parameters, "circles") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    var lines = (PluginRuntime.GetParameter(parameters, "lines") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    if (circles.Count == 0 && lines.Count == 0)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "A block needs at least one circle or line.");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      try
      {
        SymbolUtilityServices.ValidateSymbolName(name, false);
      }
      catch (Autodesk.AutoCAD.Runtime.Exception)
      {
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"'{name}' is not a valid block name.");
      }

      var blockTable = CivilObjectUtils.GetRequiredObject<BlockTable>(transaction, database.BlockTableId, OpenMode.ForRead);
      if (blockTable.Has(name))
      {
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Block '{name}' already exists; it was not modified.");
      }

      blockTable.UpgradeOpen();
      var record = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
      var blockId = blockTable.Add(record);
      transaction.AddNewlyCreatedDBObject(record, true);

      // Geometry inside the block lives on layer 0 so it takes the layer of each insertion.
      foreach (var circle in circles)
      {
        var radius = PluginRuntime.GetRequiredDouble(circle, "radius");
        if (radius <= 0)
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Circle radius must be positive.");
        var entity = new Circle(
          new Point3d(PluginRuntime.GetOptionalDouble(circle, "x") ?? 0.0, PluginRuntime.GetOptionalDouble(circle, "y") ?? 0.0, 0.0),
          Vector3d.ZAxis,
          radius);
        entity.Layer = "0";
        record.AppendEntity(entity);
        transaction.AddNewlyCreatedDBObject(entity, true);
      }

      foreach (var line in lines)
      {
        var entity = new Line(
          new Point3d(PluginRuntime.GetRequiredDouble(line, "x1"), PluginRuntime.GetRequiredDouble(line, "y1"), 0.0),
          new Point3d(PluginRuntime.GetRequiredDouble(line, "x2"), PluginRuntime.GetRequiredDouble(line, "y2"), 0.0));
        entity.Layer = "0";
        record.AppendEntity(entity);
        transaction.AddNewlyCreatedDBObject(entity, true);
      }

      return new Dictionary<string, object?>
      {
        ["blockName"] = name,
        ["handle"] = CivilObjectUtils.GetHandle(record),
        ["circles"] = circles.Count,
        ["lines"] = lines.Count,
        ["created"] = blockId != ObjectId.Null,
      };
    });
  }

  // One transaction for the whole batch: a failing item aborts everything and names the item.
  public static Task<object?> InsertBlocksAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "blockName");
    var layerName = PluginRuntime.GetOptionalString(parameters, "layer");
    var items = (PluginRuntime.GetParameter(parameters, "items") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    if (items.Count == 0)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Provide at least one item to insert.");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var blockTable = CivilObjectUtils.GetRequiredObject<BlockTable>(transaction, database.BlockTableId, OpenMode.ForRead);
      if (!blockTable.Has(name))
      {
        throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Block '{name}' does not exist; define it first.");
      }

      var blockId = blockTable[name];
      var modelSpace = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(transaction, blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
      var layerId = LookupUtils.GetLayerId(database, transaction, layerName);
      var handles = new List<string>();

      for (var index = 0; index < items.Count; index++)
      {
        var item = items[index];
        try
        {
          var position = new Point3d(
            PluginRuntime.GetRequiredDouble(item, "x"),
            PluginRuntime.GetRequiredDouble(item, "y"),
            PluginRuntime.GetOptionalDouble(item, "z") ?? 0.0);
          var reference = new BlockReference(position, blockId)
          {
            Rotation = (PluginRuntime.GetOptionalDouble(item, "rotation") ?? 0.0) * Math.PI / 180.0,
            LayerId = layerId,
          };
          var scale = PluginRuntime.GetOptionalDouble(item, "scale") ?? 1.0;
          reference.ScaleFactors = new Scale3d(scale);
          modelSpace.AppendEntity(reference);
          transaction.AddNewlyCreatedDBObject(reference, true);
          handles.Add(CivilObjectUtils.GetHandle(reference));
        }
        catch (Exception exception)
        {
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"items[{index}] failed: {exception.Message} Nothing was inserted.");
        }
      }

      return new Dictionary<string, object?>
      {
        ["blockName"] = name,
        ["layer"] = string.IsNullOrWhiteSpace(layerName) ? null : layerName,
        ["inserted"] = handles.Count,
        ["handles"] = handles,
      };
    });
  }

  public static Task<object?> ListBlocksAsync(JsonObject? parameters)
  {
    var includePositions = PluginRuntime.GetOptionalBool(parameters, "includePositions") ?? false;
    var filterName = PluginRuntime.GetOptionalString(parameters, "blockName");

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var blockTable = CivilObjectUtils.GetRequiredObject<BlockTable>(transaction, database.BlockTableId, OpenMode.ForRead);
      var blocks = new List<Dictionary<string, object?>>();
      foreach (ObjectId recordId in blockTable)
      {
        var record = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(transaction, recordId, OpenMode.ForRead);
        if (record.IsLayout || record.IsAnonymous || record.IsFromExternalReference)
          continue;
        if (!string.IsNullOrWhiteSpace(filterName) && !string.Equals(record.Name, filterName, StringComparison.OrdinalIgnoreCase))
          continue;

        var references = new List<BlockReference>();
        foreach (ObjectId referenceId in record.GetBlockReferenceIds(true, false))
        {
          references.Add(CivilObjectUtils.GetRequiredObject<BlockReference>(transaction, referenceId, OpenMode.ForRead));
        }

        var entry = new Dictionary<string, object?>
        {
          ["name"] = record.Name,
          ["referenceCount"] = references.Count,
          ["byLayer"] = references.GroupBy(r => r.Layer).ToDictionary(g => g.Key, g => g.Count()),
        };
        if (includePositions)
        {
          entry["positions"] = references
            .Select(r => new Dictionary<string, object?>
            {
              ["x"] = Math.Round(r.Position.X, 3),
              ["y"] = Math.Round(r.Position.Y, 3),
              ["layer"] = r.Layer,
            })
            .ToList();
        }
        blocks.Add(entry);
      }

      return new Dictionary<string, object?>
      {
        ["blocks"] = blocks,
      };
    });
  }
}
