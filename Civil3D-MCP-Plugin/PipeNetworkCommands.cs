using System.Collections;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace Civil3DMcpPlugin;

public static class PipeNetworkCommands
{
  public static Task<object?> ListPipeNetworksAsync()
  {
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var networks = EnumeratePipeNetworks(civilDoc, transaction, OpenMode.ForRead)
        .Select(network => ToPipeNetworkSummary(network, transaction))
        .ToList();

      return new Dictionary<string, object?>
      {
        ["networks"] = networks,
      };
    });
  }

  public static Task<object?> GetPipeNetworkAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, name, OpenMode.ForRead);
      return ToPipeNetworkDetail(network, transaction);
    });
  }

  public static Task<object?> GetPipeAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var pipeName = PluginRuntime.GetRequiredString(parameters, "pipeName");
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForRead);
      var pipe = FindPipeByName(network, transaction, pipeName, OpenMode.ForRead);
      return ToPipeData(pipe, transaction);
    });
  }

  public static Task<object?> GetStructureAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var structureName = PluginRuntime.GetRequiredString(parameters, "structureName");
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForRead);
      var structure = FindStructureByName(network, transaction, structureName, OpenMode.ForRead);
      return ToStructureData(structure, transaction);
    });
  }

  public static Task<object?> CheckPipeNetworkInterferenceAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var targetType = PluginRuntime.GetRequiredString(parameters, "targetType");
    var targetName = PluginRuntime.GetRequiredString(parameters, "targetName");

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      _ = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForRead);
      if (string.Equals(targetType, "surface", StringComparison.OrdinalIgnoreCase))
      {
        _ = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, targetName, OpenMode.ForRead);
      }
      else if (string.Equals(targetType, "pipe_network", StringComparison.OrdinalIgnoreCase))
      {
        _ = FindPipeNetworkByName(civilDoc, transaction, targetName, OpenMode.ForRead);
      }
      else
      {
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Unsupported targetType '{targetType}'.");
      }

      return new Dictionary<string, object?>
      {
        ["interferences"] = Array.Empty<object>(),
        ["totalConflicts"] = 0,
      };
    });
  }

  public static Task<object?> CreatePipeNetworkAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var partsListName = PluginRuntime.GetRequiredString(parameters, "partsList");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var networkId = CreatePipeNetwork(civilDoc, name);
      var network = CivilObjectUtils.GetRequiredObject<Network>(transaction, networkId, OpenMode.ForWrite);

      var layerName = PluginRuntime.GetOptionalString(parameters, "layer");
      if (!string.IsNullOrWhiteSpace(layerName) && network is Autodesk.AutoCAD.DatabaseServices.Entity entity)
      {
        entity.Layer = layerName;
      }

      var partsListId = FindPartsListId(civilDoc, transaction, partsListName);
      network.PartsListId = partsListId;

      var styleName = PluginRuntime.GetOptionalString(parameters, "style");
      if (!string.IsNullOrWhiteSpace(styleName))
      {
        var styleId = FindStyleId(civilDoc, transaction, styleName!, "PipeNetworkStyles", "NetworkStyles");
        if (styleId != ObjectId.Null)
        {
          network.StyleId = styleId;
        }
      }

      var referenceSurface = PluginRuntime.GetOptionalString(parameters, "referenceSurface");
      if (!string.IsNullOrWhiteSpace(referenceSurface))
      {
        var surface = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, referenceSurface!, OpenMode.ForRead);
        network.ReferenceSurfaceId = surface.ObjectId;
      }

      var referenceAlignment = PluginRuntime.GetOptionalString(parameters, "referenceAlignment");
      if (!string.IsNullOrWhiteSpace(referenceAlignment))
      {
        var alignment = CivilObjectUtils.FindAlignmentByName(civilDoc, transaction, referenceAlignment!);
        network.ReferenceAlignmentId = alignment.ObjectId;
      }

      return new Dictionary<string, object?>
      {
        ["name"] = CivilObjectUtils.GetName(network) ?? name,
        ["handle"] = CivilObjectUtils.GetHandle(network),
        ["partsList"] = ResolveObjectName(transaction, partsListId),
        ["created"] = true,
      };
    });
  }

  public static Task<object?> AddStructureToNetworkAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var x = PluginRuntime.GetRequiredDouble(parameters, "x");
    var y = PluginRuntime.GetRequiredDouble(parameters, "y");
    var partName = PluginRuntime.GetRequiredString(parameters, "partName");
    var rimElevation = PluginRuntime.GetOptionalDouble(parameters, "rimElevation") ?? 0.0;
    var sumpDepth = PluginRuntime.GetOptionalDouble(parameters, "sumpDepth") ?? 0.0;

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForWrite);
      var location = new Point3d(x, y, rimElevation);
      var createdStructureId = AddStructureToNetwork(network, transaction, location, partName, rimElevation, sumpDepth);
      var structure = CivilObjectUtils.GetRequiredObject<Structure>(transaction, createdStructureId, OpenMode.ForWrite);
      var structureName = PluginRuntime.GetOptionalString(parameters, "name");
      if (!string.IsNullOrWhiteSpace(structureName))
        structure.Name = structureName!;

      return new Dictionary<string, object?>
      {
        ["networkName"] = CivilObjectUtils.GetName(network) ?? networkName,
        ["structure"] = ToStructureData(structure, transaction),
        ["added"] = true,
      };
    });
  }

  public static Task<object?> AddPipeToNetworkAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var partName = PluginRuntime.GetRequiredString(parameters, "partName");
    var diameter = PluginRuntime.GetOptionalDouble(parameters, "diameter");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForWrite);
      var startStructureName = PluginRuntime.GetOptionalString(parameters, "startStructure");
      var endStructureName = PluginRuntime.GetOptionalString(parameters, "endStructure");
      var startStructureId = !string.IsNullOrWhiteSpace(startStructureName)
        ? FindStructureByName(network, transaction, startStructureName!, OpenMode.ForRead).ObjectId
        : ObjectId.Null;
      var endStructureId = !string.IsNullOrWhiteSpace(endStructureName)
        ? FindStructureByName(network, transaction, endStructureName!, OpenMode.ForRead).ObjectId
        : ObjectId.Null;

      var startPoint = ReadPoint(parameters, "startPoint");
      var endPoint = ReadPoint(parameters, "endPoint");
      var createdPipeId = AddPipeToNetwork(network, transaction, partName, diameter, startPoint, endPoint, startStructureId, endStructureId);
      var pipe = CivilObjectUtils.GetRequiredObject<Pipe>(transaction, createdPipeId, OpenMode.ForWrite);
      var pipeName = PluginRuntime.GetOptionalString(parameters, "name");
      if (!string.IsNullOrWhiteSpace(pipeName))
        pipe.Name = pipeName!;

      return new Dictionary<string, object?>
      {
        ["networkName"] = CivilObjectUtils.GetName(network) ?? networkName,
        ["pipe"] = ToPipeData(pipe, transaction),
        ["added"] = true,
      };
    });
  }

  public static Task<object?> ResizePipeInNetworkAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var pipeName = PluginRuntime.GetRequiredString(parameters, "pipeName");
    var newPartName = PluginRuntime.GetOptionalString(parameters, "newPartName");
    var newDiameter = PluginRuntime.GetOptionalDouble(parameters, "newDiameter");

    if (string.IsNullOrWhiteSpace(newPartName) && !newDiameter.HasValue)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Either 'newPartName' or 'newDiameter' is required.");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForWrite);
      var pipe = FindPipeByName(network, transaction, pipeName, OpenMode.ForWrite);

      if (!string.IsNullOrWhiteSpace(newPartName))
      {
        var part = FindPartForNetwork(network, transaction, newPartName!, DomainType.Pipe);
        pipe.SwapPartFamilyAndSize(part.FamilyId, part.SizeId);
      }

      if (newDiameter.HasValue)
      {
        pipe.ResizeByInnerDiameterOrWidth(newDiameter.Value, useClosestSize: false);
      }

      return new Dictionary<string, object?>
      {
        ["networkName"] = networkName,
        ["pipeName"] = pipeName,
        ["newPartName"] = newPartName,
        ["newDiameter"] = newDiameter,
        ["resized"] = true,
      };
    });
  }

  // Batch add: every structure, then every pipe, in one transaction, so a failing item leaves nothing behind.
  // Pipes may reference structures created earlier in the same batch or already in the network.
  public static Task<object?> AddPartsToNetworkAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var structureItems = (PluginRuntime.GetParameter(parameters, "structures") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    var pipeItems = (PluginRuntime.GetParameter(parameters, "pipes") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    if (structureItems.Count == 0 && pipeItems.Count == 0)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Provide at least one item in 'structures' or 'pipes'.");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForWrite);
      var createdStructures = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
      var structureResults = new List<Dictionary<string, object?>>();
      var pipeResults = new List<Dictionary<string, object?>>();

      for (var index = 0; index < structureItems.Count; index++)
      {
        var item = structureItems[index];
        try
        {
          var x = PluginRuntime.GetRequiredDouble(item, "x");
          var y = PluginRuntime.GetRequiredDouble(item, "y");
          var partName = PluginRuntime.GetRequiredString(item, "partName");
          var rimElevation = PluginRuntime.GetOptionalDouble(item, "rimElevation") ?? 0.0;
          var sumpDepth = PluginRuntime.GetOptionalDouble(item, "sumpDepth") ?? 0.0;
          var structureId = AddStructureToNetwork(network, transaction, new Point3d(x, y, rimElevation), partName, rimElevation, sumpDepth);
          var structure = CivilObjectUtils.GetRequiredObject<Structure>(transaction, structureId, OpenMode.ForWrite);
          var name = PluginRuntime.GetOptionalString(item, "name");
          if (!string.IsNullOrWhiteSpace(name))
            structure.Name = name!;
          createdStructures[structure.Name] = structureId;
          structureResults.Add(ToStructureData(structure, transaction));
        }
        catch (Exception exception) when (exception is not JsonRpcDispatchException)
        {
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"structures[{index}] failed: {exception.Message}. Nothing was added.");
        }
        catch (JsonRpcDispatchException exception)
        {
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"structures[{index}] failed: {exception.Message} Nothing was added.");
        }
      }

      ObjectId ResolveStructureId(string? name)
      {
        if (string.IsNullOrWhiteSpace(name))
          return ObjectId.Null;
        return createdStructures.TryGetValue(name!, out var id)
          ? id
          : FindStructureByName(network, transaction, name!, OpenMode.ForRead).ObjectId;
      }

      for (var index = 0; index < pipeItems.Count; index++)
      {
        var item = pipeItems[index];
        try
        {
          var partName = PluginRuntime.GetRequiredString(item, "partName");
          var startStructureId = ResolveStructureId(PluginRuntime.GetOptionalString(item, "startStructure"));
          var endStructureId = ResolveStructureId(PluginRuntime.GetOptionalString(item, "endStructure"));
          var pipeId = AddPipeToNetwork(
            network,
            transaction,
            partName,
            PluginRuntime.GetOptionalDouble(item, "diameter"),
            ReadPoint(item, "startPoint"),
            ReadPoint(item, "endPoint"),
            startStructureId,
            endStructureId);
          var pipe = CivilObjectUtils.GetRequiredObject<Pipe>(transaction, pipeId, OpenMode.ForWrite);
          var name = PluginRuntime.GetOptionalString(item, "name");
          if (!string.IsNullOrWhiteSpace(name))
            pipe.Name = name!;
          pipeResults.Add(ToPipeData(pipe, transaction));
        }
        catch (Exception exception) when (exception is not JsonRpcDispatchException)
        {
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"pipes[{index}] failed: {exception.Message}. Nothing was added.");
        }
        catch (JsonRpcDispatchException exception)
        {
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"pipes[{index}] failed: {exception.Message} Nothing was added.");
        }
      }

      return new Dictionary<string, object?>
      {
        ["networkName"] = CivilObjectUtils.GetName(network) ?? networkName,
        ["addedStructures"] = structureResults.Count,
        ["addedPipes"] = pipeResults.Count,
        ["structures"] = structureResults,
        ["pipes"] = pipeResults,
      };
    });
  }

  // The API has no Network.Remove*: parts are erased as AutoCAD objects. Pipes are disconnected first so
  // structures do not keep dangling connections.
  public static Task<object?> DeletePipeNetworkAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, name, OpenMode.ForWrite);
      var pipeCount = 0;
      foreach (ObjectId pipeId in network.GetPipeIds())
      {
        ErasePipe(CivilObjectUtils.GetRequiredObject<Pipe>(transaction, pipeId, OpenMode.ForWrite));
        pipeCount++;
      }

      var structureCount = 0;
      foreach (ObjectId structureId in network.GetStructureIds())
      {
        CivilObjectUtils.GetRequiredObject<Structure>(transaction, structureId, OpenMode.ForWrite).Erase();
        structureCount++;
      }

      network.Erase();
      return new Dictionary<string, object?>
      {
        ["name"] = name,
        ["deletedPipes"] = pipeCount,
        ["deletedStructures"] = structureCount,
        ["deleted"] = true,
      };
    });
  }

  public static Task<object?> DeletePipeFromNetworkAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var pipeName = PluginRuntime.GetRequiredString(parameters, "pipeName");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForRead);
      var pipe = FindPipeByName(network, transaction, pipeName, OpenMode.ForWrite);
      var startStructure = ResolveObjectName(transaction, pipe.StartStructureId);
      var endStructure = ResolveObjectName(transaction, pipe.EndStructureId);
      ErasePipe(pipe);
      return new Dictionary<string, object?>
      {
        ["networkName"] = networkName,
        ["pipeName"] = pipeName,
        ["disconnectedFrom"] = new[] { startStructure, endStructure }.Where(s => s != null).ToList(),
        ["deleted"] = true,
      };
    });
  }

  public static Task<object?> DeleteStructureFromNetworkAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var structureName = PluginRuntime.GetRequiredString(parameters, "structureName");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName, OpenMode.ForRead);
      var structure = FindStructureByName(network, transaction, structureName, OpenMode.ForWrite);
      if (structure.ConnectedPipesCount > 0)
      {
        throw new JsonRpcDispatchException(
          "CIVIL3D.INVALID_INPUT",
          $"Structure '{structureName}' still has connected pipes ({string.Join(", ", structure.GetConnectedPipeNames())}); delete them first.");
      }

      structure.Erase();
      return new Dictionary<string, object?>
      {
        ["networkName"] = networkName,
        ["structureName"] = structureName,
        ["deleted"] = true,
      };
    });
  }

  private static void ErasePipe(Pipe pipe)
  {
    if (!pipe.StartStructureId.IsNull)
      pipe.Disconnect(ConnectorPositionType.Start);
    if (!pipe.EndStructureId.IsNull)
      pipe.Disconnect(ConnectorPositionType.End);
    pipe.Erase();
  }

  public static Task<object?> ListPipePartsCatalogAsync(JsonObject? parameters)
  {
    var partsListName = PluginRuntime.GetOptionalString(parameters, "partsList");

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var partsLists = EnumeratePartsLists(civilDoc, transaction)
        .Where(partsList => string.IsNullOrWhiteSpace(partsListName) || string.Equals(CivilObjectUtils.GetName(partsList), partsListName, StringComparison.OrdinalIgnoreCase))
        .Select(partsList =>
        {
          var pipeParts = EnumeratePartSizeNames(partsList, transaction, DomainType.Pipe).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
          var structureParts = EnumeratePartSizeNames(partsList, transaction, DomainType.Structure).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
          return new Dictionary<string, object?>
          {
            ["name"] = CivilObjectUtils.GetName(partsList),
            ["handle"] = CivilObjectUtils.GetHandle(partsList),
            ["parts"] = pipeParts.Concat(structureParts).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList(),
            ["pipeParts"] = pipeParts,
            ["structureParts"] = structureParts,
          };
        })
        .ToList();

      return new Dictionary<string, object?>
      {
        ["partsLists"] = partsLists,
      };
    });
  }

  public static int CountPipeNetworks(object civilDoc)
  {
    return ((CivilDocument)civilDoc).GetPipeNetworkIds().Count;
  }

  public static string? GetFirstPipeNetworkStyleName(object civilDoc, Transaction transaction)
  {
    var styles = ((CivilDocument)civilDoc).Styles;
    var collection = Civil3DCompatibility.GetPropertyValue(styles, "PipeNetworkStyles")
      ?? Civil3DCompatibility.GetPropertyValue(styles, "NetworkStyles");
    return LookupUtils.GetFirstStyleName(collection, transaction);
  }

  private static IEnumerable<Network> EnumeratePipeNetworks(object civilDoc, Transaction transaction, OpenMode openMode)
  {
    foreach (ObjectId objectId in ((CivilDocument)civilDoc).GetPipeNetworkIds())
    {
      yield return CivilObjectUtils.GetRequiredObject<Network>(transaction, objectId, openMode);
    }
  }

  private static IEnumerable<ObjectId> GetPipeNetworkIds(object civilDoc)
  {
    var candidates = new[]
    {
      CivilObjectUtils.InvokeMethod(civilDoc, "GetPipeNetworkIds"),
      GetNamedMemberValue(civilDoc, "PipeNetworkCollection"),
      GetNamedMemberValue(civilDoc, "NetworkCollection"),
      GetNamedMemberValue(civilDoc, "PipeNetworks"),
      GetNamedMemberValue(civilDoc, "Networks"),
    };

    foreach (var candidate in candidates)
    {
      foreach (var objectId in ToObjectIdsFlexible(candidate))
      {
        if (objectId != ObjectId.Null)
        {
          yield return objectId;
        }
      }
    }
  }

  private static IEnumerable<ObjectId> ToObjectIdsFlexible(object? value)
  {
    foreach (var objectId in CivilObjectUtils.ToObjectIds(value))
    {
      yield return objectId;
    }

    if (value is IEnumerable enumerable)
    {
      foreach (var item in enumerable)
      {
        if (item is ObjectId objectId && objectId != ObjectId.Null)
        {
          yield return objectId;
          continue;
        }

        var itemObjectId = GetAnyObjectId(item, "ObjectId", "Id");
        if (itemObjectId != ObjectId.Null)
        {
          yield return itemObjectId;
        }
      }
    }
  }

  private static Network FindPipeNetworkByName(object civilDoc, Transaction transaction, string name, OpenMode openMode)
  {
    foreach (var network in EnumeratePipeNetworks(civilDoc, transaction, openMode))
    {
      if (string.Equals(CivilObjectUtils.GetName(network), name, StringComparison.OrdinalIgnoreCase))
      {
        return network;
      }
    }

    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Pipe network '{name}' was not found.");
  }

  private static Pipe FindPipeByName(Network network, Transaction transaction, string pipeName, OpenMode openMode)
  {
    foreach (ObjectId objectId in network.GetPipeIds())
    {
      var pipe = CivilObjectUtils.GetRequiredObject<Pipe>(transaction, objectId, openMode);
      if (string.Equals(pipe.Name, pipeName, StringComparison.OrdinalIgnoreCase))
      {
        return pipe;
      }
    }

    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Pipe '{pipeName}' was not found in network '{CivilObjectUtils.GetName(network)}'.");
  }

  private static Structure FindStructureByName(Network network, Transaction transaction, string structureName, OpenMode openMode)
  {
    foreach (ObjectId objectId in network.GetStructureIds())
    {
      var structure = CivilObjectUtils.GetRequiredObject<Structure>(transaction, objectId, openMode);
      if (string.Equals(structure.Name, structureName, StringComparison.OrdinalIgnoreCase))
      {
        return structure;
      }
    }

    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Structure '{structureName}' was not found in network '{CivilObjectUtils.GetName(network)}'.");
  }

  private static Dictionary<string, object?> ToPipeNetworkSummary(Network network, Transaction transaction)
  {
    return new Dictionary<string, object?>
    {
      ["name"] = network.Name,
      ["handle"] = CivilObjectUtils.GetHandle(network),
      ["pipeCount"] = network.GetPipeIds().Count,
      ["structureCount"] = network.GetStructureIds().Count,
      ["surface"] = ResolveObjectName(transaction, network.ReferenceSurfaceId),
    };
  }

  private static Dictionary<string, object?> ToPipeNetworkDetail(Network network, Transaction transaction)
  {
    var pipes = network.GetPipeIds().Cast<ObjectId>()
      .Select(objectId => ToPipeData(CivilObjectUtils.GetRequiredObject<Pipe>(transaction, objectId, OpenMode.ForRead), transaction))
      .ToList();
    var structures = network.GetStructureIds().Cast<ObjectId>()
      .Select(objectId => ToStructureData(CivilObjectUtils.GetRequiredObject<Structure>(transaction, objectId, OpenMode.ForRead), transaction))
      .ToList();

    return new Dictionary<string, object?>
    {
      ["name"] = network.Name,
      ["handle"] = CivilObjectUtils.GetHandle(network),
      ["partsList"] = ResolveObjectName(transaction, network.PartsListId),
      ["style"] = network.StyleName,
      ["referenceSurface"] = ResolveObjectName(transaction, network.ReferenceSurfaceId),
      ["referenceAlignment"] = ResolveObjectName(transaction, network.ReferenceAlignmentId),
      ["pipes"] = pipes,
      ["structures"] = structures,
    };
  }

  private static Dictionary<string, object?> ToPipeData(Pipe pipe, Transaction transaction)
  {
    return new Dictionary<string, object?>
    {
      ["name"] = pipe.Name,
      ["handle"] = CivilObjectUtils.GetHandle(pipe),
      ["startStructure"] = ResolveObjectName(transaction, pipe.StartStructureId),
      ["endStructure"] = ResolveObjectName(transaction, pipe.EndStructureId),
      ["length"] = pipe.Length3D,
      ["diameter"] = pipe.InnerDiameterOrWidth,
      ["slope"] = pipe.Slope,
      ["material"] = pipe.Material,
      ["centerlineStartElevation"] = pipe.StartPoint.Z,
      ["centerlineEndElevation"] = pipe.EndPoint.Z,
      ["invertIn"] = null,
      ["invertOut"] = null,
      ["invertNote"] = "The Civil 3D 2026 managed Pipe API does not expose endpoint invert elevations directly; centerline elevations are returned instead.",
    };
  }

  private static Dictionary<string, object?> ToStructureData(Structure structure, Transaction transaction)
  {
    var connectedPipeIds = Enumerable.Range(0, structure.ConnectedPipesCount)
      .Select(index => Civil3DCompatibility.GetIndexedPropertyValue(structure, "ConnectedPipe", index))
      .OfType<ObjectId>();

    return new Dictionary<string, object?>
    {
      ["name"] = structure.Name,
      ["handle"] = CivilObjectUtils.GetHandle(structure),
      ["type"] = structure.PartType.ToString(),
      ["rimElevation"] = structure.RimElevation,
      ["sumpElevation"] = structure.SumpElevation,
      ["x"] = structure.Location.X,
      ["y"] = structure.Location.Y,
      ["connectedPipes"] = connectedPipeIds.Select(objectId => ResolveObjectName(transaction, objectId) ?? objectId.Handle.ToString()).ToList(),
    };
  }

  private static ObjectId CreatePipeNetwork(object civilDoc, string name)
  {
    var requestedName = name;
    return Network.Create((CivilDocument)civilDoc, ref requestedName);
  }

  private static ObjectId AddStructureToNetwork(Network network, Transaction transaction, Point3d location, string partName, double rimElevation, double sumpDepth)
  {
    var part = FindPartForNetwork(network, transaction, partName, DomainType.Structure);
    var createdId = ObjectId.Null;
    network.AddStructure(part.FamilyId, part.SizeId, location, 0.0, ref createdId, applyRules: false);
    var structure = CivilObjectUtils.GetRequiredObject<Structure>(transaction, createdId, OpenMode.ForWrite);
    structure.RimElevation = rimElevation;
    // Only junction structures (manholes) have a sump; headwalls/outlets reject RimToSumpHeight.
    if (structure.PartType == PartType.StructJunction)
      structure.RimToSumpHeight = sumpDepth;
    return createdId;
  }

  private static ObjectId AddPipeToNetwork(Network network, Transaction transaction, string partName, double? diameter, Point3d? startPoint, Point3d? endPoint, ObjectId startStructureId, ObjectId endStructureId)
  {
    var start = startPoint ?? GetStructureLocation(transaction, startStructureId, "start");
    var end = endPoint ?? GetStructureLocation(transaction, endStructureId, "end");
    var part = FindPartForNetwork(network, transaction, partName, DomainType.Pipe);
    var createdId = ObjectId.Null;
    network.AddLinePipe(part.FamilyId, part.SizeId, new LineSegment3d(start, end), ref createdId, applyRules: false);
    var pipe = CivilObjectUtils.GetRequiredObject<Pipe>(transaction, createdId, OpenMode.ForWrite);
    if (startStructureId != ObjectId.Null)
      pipe.ConnectToStructure(ConnectorPositionType.Start, startStructureId, force: true);
    if (endStructureId != ObjectId.Null)
      pipe.ConnectToStructure(ConnectorPositionType.End, endStructureId, force: true);
    if (diameter.HasValue && Math.Abs(pipe.InnerDiameterOrWidth - diameter.Value) > 1.0e-6)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Part size '{partName}' has inner diameter/width {pipe.InnerDiameterOrWidth}, not the requested {diameter.Value}. The pipe was not committed.");
    return createdId;
  }

  private static IEnumerable<ObjectId> GetChildObjectIds(object owner, params string[] memberNames)
  {
    foreach (var memberName in memberNames)
    {
      object? memberValue = CivilObjectUtils.InvokeMethod(owner, memberName) ?? GetNamedMemberValue(owner, memberName);
      foreach (var objectId in ToObjectIdsFlexible(memberValue))
      {
        if (objectId != ObjectId.Null)
        {
          yield return objectId;
        }
      }
    }
  }

  private static object? GetNamedMemberValue(object? value, string memberName)
  {
    return Civil3DCompatibility.GetPropertyValue(value, memberName)
      ?? Civil3DCompatibility.GetFieldValue(value, memberName);
  }

  private static string? ResolveObjectName(Transaction transaction, ObjectId objectId)
  {
    if (objectId == ObjectId.Null)
    {
      return null;
    }

    try
    {
      var dbObject = transaction.GetObject(objectId, OpenMode.ForRead);
      return CivilObjectUtils.GetName(dbObject);
    }
    catch
    {
      return null;
    }
  }

  private static ObjectId GetAnyObjectId(object? value, params string[] propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      var objectId = CivilObjectUtils.GetPropertyValue<ObjectId>(value, propertyName);
      if (objectId != ObjectId.Null)
      {
        return objectId;
      }
    }

    return ObjectId.Null;
  }

  private static double? GetAnyDouble(object? value, params string[] propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      var propertyValue = CivilObjectUtils.GetPropertyValue<double?>(value, propertyName);
      if (propertyValue.HasValue)
      {
        return propertyValue.Value;
      }
    }

    return null;
  }

  private static string? GetAnyString(object? value, params string[] propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      var propertyValue = CivilObjectUtils.GetStringProperty(value, propertyName);
      if (!string.IsNullOrWhiteSpace(propertyValue))
      {
        return propertyValue;
      }
    }

    return null;
  }

  private static Point3d? GetPointProperty(object? value, params string[] propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      var propertyValue = CivilObjectUtils.GetPropertyValue<Point3d?>(value, propertyName);
      if (propertyValue.HasValue)
      {
        return propertyValue.Value;
      }
    }

    return null;
  }

  private static double Distance(Point3d? startPoint, Point3d? endPoint)
  {
    if (!startPoint.HasValue || !endPoint.HasValue)
    {
      return 0.0;
    }

    return startPoint.Value.DistanceTo(endPoint.Value);
  }

  private static double CalculateSlope(Point3d? startPoint, Point3d? endPoint)
  {
    if (!startPoint.HasValue || !endPoint.HasValue)
    {
      return 0.0;
    }

    var horizontal = Math.Sqrt(Math.Pow(endPoint.Value.X - startPoint.Value.X, 2) + Math.Pow(endPoint.Value.Y - startPoint.Value.Y, 2));
    if (horizontal <= 1.0e-9)
    {
      return 0.0;
    }

    return (endPoint.Value.Z - startPoint.Value.Z) / horizontal;
  }

  private static Point3d? ReadPoint(JsonObject? parameters, string parameterName)
  {
    if (PluginRuntime.GetParameter(parameters, parameterName) is not JsonObject pointNode)
    {
      return null;
    }

    var x = pointNode["x"]?.GetValue<double>() ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"{parameterName}.x is required.");
    var y = pointNode["y"]?.GetValue<double>() ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"{parameterName}.y is required.");
    var z = pointNode["z"]?.GetValue<double>() ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"{parameterName}.z is required; elevation 0 will not be assumed.");
    return new Point3d(x, y, z);
  }

  private static ObjectId FindStyleId(object civilDoc, Transaction transaction, string styleName, params string[] collectionNames)
  {
    var styles = GetNamedMemberValue(civilDoc, "Styles");
    if (styles == null)
    {
      return ObjectId.Null;
    }

    foreach (var collectionName in collectionNames)
    {
      var collection = GetNamedMemberValue(styles, collectionName);
      foreach (var objectId in CivilObjectUtils.ToObjectIds(collection))
      {
        if (objectId == ObjectId.Null)
        {
          continue;
        }

        var style = transaction.GetObject(objectId, OpenMode.ForRead);
        if (string.Equals(CivilObjectUtils.GetName(style), styleName, StringComparison.OrdinalIgnoreCase))
        {
          return objectId;
        }
      }
    }

    return ObjectId.Null;
  }

  private static ObjectId FindPartsListId(object civilDoc, Transaction transaction, string partsListName)
  {
    foreach (var partsList in EnumeratePartsLists(civilDoc, transaction))
    {
      if (string.Equals(CivilObjectUtils.GetName(partsList), partsListName, StringComparison.OrdinalIgnoreCase))
      {
        return partsList.ObjectId;
      }
    }

    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Parts list '{partsListName}' was not found.");
  }

  private static IEnumerable<PartsList> EnumeratePartsLists(object civilDoc, Transaction transaction)
  {
    foreach (ObjectId objectId in ((CivilDocument)civilDoc).Styles.PartsListSet)
    {
      if (!objectId.IsNull)
      {
        yield return CivilObjectUtils.GetRequiredObject<PartsList>(transaction, objectId, OpenMode.ForRead);
      }
    }
  }

  private static IEnumerable<string> EnumeratePartSizeNames(PartsList partsList, Transaction transaction, DomainType domain)
  {
    foreach (ObjectId familyId in partsList.GetPartFamilyIdsByDomain(domain))
    {
      var family = CivilObjectUtils.GetRequiredObject<PartFamily>(transaction, familyId, OpenMode.ForRead);
      for (var index = 0; index < family.PartSizeCount; index++)
      {
        var sizeName = CivilObjectUtils.GetName(transaction.GetObject(family[index], OpenMode.ForRead));
        if (!string.IsNullOrWhiteSpace(sizeName))
        {
          yield return sizeName!;
        }
      }
    }
  }

  private readonly record struct NetworkPartIds(ObjectId FamilyId, ObjectId SizeId);

  private static NetworkPartIds FindPartForNetwork(Network network, Transaction transaction, string partName, DomainType domain)
  {
    if (network.PartsListId.IsNull)
    {
      throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Pipe network '{network.Name}' does not have a parts list assigned.");
    }

    var partsList = CivilObjectUtils.GetRequiredObject<PartsList>(transaction, network.PartsListId, OpenMode.ForRead);
    foreach (ObjectId familyId in partsList.GetPartFamilyIdsByDomain(domain))
    {
      var family = CivilObjectUtils.GetRequiredObject<PartFamily>(transaction, familyId, OpenMode.ForRead);
      for (var index = 0; index < family.PartSizeCount; index++)
      {
        var sizeId = family[index];
        var size = transaction.GetObject(sizeId, OpenMode.ForRead);
        var sizeName = CivilObjectUtils.GetName(size) ?? CivilObjectUtils.GetStringProperty(size, "Description");
        if (string.Equals(sizeName, partName, StringComparison.OrdinalIgnoreCase)
          || (family.PartSizeCount == 1 && string.Equals(family.Name, partName, StringComparison.OrdinalIgnoreCase)))
          return new NetworkPartIds(familyId, sizeId);
      }
    }

    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Exact {domain} size '{partName}' was not found in the parts list for network '{network.Name}'.");
  }

  private static Point3d GetStructureLocation(Transaction transaction, ObjectId structureId, string endpointName)
  {
    if (structureId.IsNull)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Provide {endpointName}Point or {endpointName}Structure; an origin point will not be assumed.");
    return CivilObjectUtils.GetRequiredObject<Structure>(transaction, structureId, OpenMode.ForRead).Location;
  }
}
