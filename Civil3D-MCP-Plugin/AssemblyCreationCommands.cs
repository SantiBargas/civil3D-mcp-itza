using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace Civil3DMcpPlugin;

/// <summary>
/// Assembly operations implemented with the documented Civil 3D 2026 API.
/// Dynamic stock-subassembly parameters are the only compatibility access.
/// </summary>
public static class AssemblyCreationCommands
{
  public static Task<object?> ListAssembliesAsync()
  {
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var assemblies = civilDoc.AssemblyCollection
        .Select(id => CivilObjectUtils.GetRequiredObject<Assembly>(transaction, id, OpenMode.ForRead))
        .Select(assembly =>
        {
          var summary = ToAssemblySummary(assembly);
          summary["usedByCorridors"] = new List<string>();
          // Layers of the assembly and its subassemblies, to freeze them in sheet viewports.
          var layers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { assembly.Layer };
          foreach (ObjectId subassemblyId in GetSubassemblyIds(assembly))
          {
            if (transaction.GetObject(subassemblyId, OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Entity subassembly)
              layers.Add(subassembly.Layer);
          }
          summary["layers"] = layers.ToList();
          return summary;
        })
        .ToList();

      return new Dictionary<string, object?> { ["assemblies"] = assemblies };
    });
  }

  public static Task<object?> GetAssemblyAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var assembly = FindAssemblyByName(civilDoc, transaction, name, OpenMode.ForRead);
      var usedByCorridors = GetCorridorsUsingAssembly(civilDoc, transaction, assembly.ObjectId);

      return new Dictionary<string, object?>
      {
        ["name"] = assembly.Name,
        ["handle"] = CivilObjectUtils.GetHandle(assembly),
        ["subassemblyCount"] = GetSubassemblyIds(assembly).Count,
        ["style"] = GetStyleName(assembly, transaction),
        ["type"] = assembly.Type.ToString(),
        ["subassemblies"] = GetSubassemblies(assembly, transaction),
        ["usedByCorridors"] = usedByCorridors,
      };
    });
  }

  public static Task<object?> DeleteAssemblyAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var assembly = FindAssemblyByName(civilDoc, transaction, name, OpenMode.ForWrite);
      var usedByCorridors = GetCorridorsUsingAssembly(civilDoc, transaction, assembly.ObjectId);
      if (usedByCorridors.Count > 0)
        throw new JsonRpcDispatchException(
          "CIVIL3D.INVALID_INPUT",
          $"Assembly '{name}' is used by corridor(s) {string.Join(", ", usedByCorridors)}. Delete or edit those regions first.");

      var subassemblyCount = GetSubassemblyIds(assembly).Count;
      assembly.Erase();
      return new Dictionary<string, object?>
      {
        ["name"] = name,
        ["subassemblyCount"] = subassemblyCount,
        ["deleted"] = true,
      };
    });
  }

  private static List<string> GetCorridorsUsingAssembly(CivilDocument civilDoc, Transaction transaction, ObjectId assemblyId)
  {
    var usedByCorridors = new List<string>();
    foreach (ObjectId corridorId in civilDoc.CorridorCollection)
    {
      var corridor = CivilObjectUtils.GetRequiredObject<Corridor>(transaction, corridorId, OpenMode.ForRead);
      foreach (Baseline baseline in corridor.Baselines)
      {
        foreach (BaselineRegion region in baseline.BaselineRegions)
        {
          if (region.AssemblyId == assemblyId && !usedByCorridors.Contains(corridor.Name))
            usedByCorridors.Add(corridor.Name);
        }
      }
    }
    return usedByCorridors;
  }

  public static Task<object?> CreateAssemblyAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var insertX = PluginRuntime.GetRequiredDouble(parameters, "insertX");
    var insertY = PluginRuntime.GetRequiredDouble(parameters, "insertY");
    var description = PluginRuntime.GetOptionalString(parameters, "description") ?? string.Empty;
    var assemblyTypeText = PluginRuntime.GetRequiredString(parameters, "assemblyType");
    if (!Enum.TryParse<AssemblyType>(assemblyTypeText, ignoreCase: true, out var assemblyType))
    {
      throw new JsonRpcDispatchException(
        "CIVIL3D.INVALID_INPUT",
        $"Invalid assemblyType '{assemblyTypeText}'. Use {string.Join(", ", Enum.GetNames<AssemblyType>())}.");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var assemblyId = civilDoc.AssemblyCollection.Add(name, assemblyType, new Point3d(insertX, insertY, 0));
      var assembly = CivilObjectUtils.GetRequiredObject<Assembly>(transaction, assemblyId, OpenMode.ForWrite);
      assembly.Description = description;

      return new Dictionary<string, object?>
      {
        ["name"] = assembly.Name,
        ["handle"] = CivilObjectUtils.GetHandle(assembly),
        ["insertX"] = insertX,
        ["insertY"] = insertY,
        ["assemblyType"] = assembly.Type.ToString(),
        ["created"] = true,
      };
    });
  }

  public static Task<object?> CreateSubassemblyAsync(JsonObject? parameters)
  {
    var assemblyName = PluginRuntime.GetRequiredString(parameters, "assemblyName");
    var subassemblyType = PluginRuntime.GetRequiredString(parameters, "subassemblyType");
    var side = PluginRuntime.GetRequiredString(parameters, "side");
    if (!new[] { "Left", "Right", "Both" }.Contains(side, StringComparer.OrdinalIgnoreCase))
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "side must be Left, Right, or Both.");

    var baseName = PluginRuntime.GetOptionalString(parameters, "name");
    var attachTo = PluginRuntime.GetOptionalString(parameters, "attachTo");
    var attachPointIndex = PluginRuntime.GetOptionalInt(parameters, "attachPointIndex");

    var subParams = ReadParameters(parameters?["parameters"] as JsonObject);
    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var assembly = FindAssemblyByName(civilDoc, transaction, assemblyName, OpenMode.ForWrite);
      var requestedSides = side.Equals("Both", StringComparison.OrdinalIgnoreCase)
        ? new[] { "Left", "Right" }
        : new[] { side };
      var created = new List<Dictionary<string, object?>>();

      foreach (var requestedSide in requestedSides)
      {
        var subassemblyName = string.IsNullOrWhiteSpace(baseName)
          ? $"{subassemblyType}-{requestedSide}-{Guid.NewGuid():N}"
          : requestedSides.Length > 1 ? $"{baseName}-{requestedSide}" : baseName!;
        var subassemblyId = civilDoc.SubassemblyCollection.ImportStockSubassembly(
          subassemblyName,
          subassemblyType,
          assembly.Location);

        int? hookedPointIndex = null;
        if (string.IsNullOrWhiteSpace(attachTo))
        {
          assembly.AddSubassembly(subassemblyId);
        }
        else
        {
          // AddSubassembly(id) alone always starts a new group at the baseline point;
          // chaining needs the overload that takes a point of the parent subassembly.
          var parent = FindAttachParent(assembly, transaction, attachTo!, requestedSide, requestedSides.Length > 1);
          var hookPoint = ResolveHookPoint(parent, attachPointIndex);
          assembly.AddSubassembly(subassemblyId, hookPoint);
          hookedPointIndex = hookPoint.Index;
        }

        var subassembly = CivilObjectUtils.GetRequiredObject<AcDbObject>(transaction, subassemblyId, OpenMode.ForWrite);
        if (!Civil3DCompatibility.TrySetProperty(subassembly, "Side", requestedSide))
        {
          throw new JsonRpcDispatchException(
            "CIVIL3D.API_ERROR",
            $"Stock subassembly '{subassemblyType}' does not expose a writable Side parameter. No implicit side was assumed.");
        }
        var applied = ApplySubassemblyParameters(subassembly, subParams);
        if (applied.Count != subParams.Count)
        {
          var missing = subParams.Keys.Except(applied, StringComparer.OrdinalIgnoreCase);
          throw new JsonRpcDispatchException(
            "CIVIL3D.INVALID_INPUT",
            $"Subassembly '{subassemblyType}' has no writable parameter(s): {string.Join(", ", missing)}. The transaction was not committed.");
        }

        created.Add(new Dictionary<string, object?>
        {
          ["name"] = CivilObjectUtils.GetName(subassembly) ?? subassemblyName,
          ["handle"] = CivilObjectUtils.GetHandle(subassembly),
          ["side"] = requestedSide,
          ["attachedTo"] = string.IsNullOrWhiteSpace(attachTo) ? null : attachTo,
          ["attachPointIndex"] = hookedPointIndex,
        });
      }

      return new Dictionary<string, object?>
      {
        ["assemblyName"] = assemblyName,
        ["subassemblyType"] = subassemblyType,
        ["subassemblies"] = created,
        ["added"] = true,
      };
    });
  }

  public static Task<object?> EditAssemblyAsync(JsonObject? parameters)
  {
    var assemblyName = PluginRuntime.GetRequiredString(parameters, "assemblyName");
    var subassemblyName = PluginRuntime.GetOptionalString(parameters, "subassemblyName");
    var deleteSubassembly = PluginRuntime.GetOptionalBool(parameters, "delete") ?? false;
    var editParameters = ReadParameters(parameters?["parameters"] as JsonObject);

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var openMode = deleteSubassembly || editParameters.Count > 0 ? OpenMode.ForWrite : OpenMode.ForRead;
      var assembly = FindAssemblyByName(civilDoc, transaction, assemblyName, openMode);
      var subassemblyIds = GetSubassemblyIds(assembly);

      if (string.IsNullOrWhiteSpace(subassemblyName))
      {
        var subassemblies = subassemblyIds
          .Select(id => CivilObjectUtils.GetRequiredObject<AcDbObject>(transaction, id, OpenMode.ForRead))
          .Select(ToSubassemblySummary)
          .ToList();
        return new Dictionary<string, object?>
        {
          ["assemblyName"] = assemblyName,
          ["subassemblyCount"] = subassemblies.Count,
          ["subassemblies"] = subassemblies,
        };
      }

      var targetId = subassemblyIds.FirstOrDefault(id =>
      {
        var candidate = CivilObjectUtils.GetRequiredObject<AcDbObject>(transaction, id, OpenMode.ForRead);
        return string.Equals(CivilObjectUtils.GetName(candidate), subassemblyName, StringComparison.OrdinalIgnoreCase);
      });
      if (targetId.IsNull)
        throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Subassembly '{subassemblyName}' not found in assembly '{assemblyName}'.");

      var target = CivilObjectUtils.GetRequiredObject<AcDbObject>(transaction, targetId, OpenMode.ForWrite);
      if (deleteSubassembly)
      {
        target.Erase();
        return new Dictionary<string, object?>
        {
          ["assemblyName"] = assemblyName,
          ["subassemblyName"] = subassemblyName,
          ["deleted"] = true,
        };
      }

      var updated = ApplySubassemblyParameters(target, editParameters);
      if (editParameters.Count > 0 && updated.Count != editParameters.Count)
      {
        var missing = editParameters.Keys.Except(updated, StringComparer.OrdinalIgnoreCase);
        throw new JsonRpcDispatchException(
          "CIVIL3D.INVALID_INPUT",
          $"Subassembly parameters were not writable: {string.Join(", ", missing)}. The transaction was not committed.");
      }

      return new Dictionary<string, object?>
      {
        ["assemblyName"] = assemblyName,
        ["subassemblyName"] = subassemblyName,
        ["updatedParameters"] = updated,
        ["updated"] = updated.Count > 0,
      };
    });
  }

  private static Dictionary<string, object?> ReadParameters(JsonObject? values)
  {
    var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    if (values == null) return result;
    foreach (var pair in values)
      result[pair.Key] = pair.Value?.GetValue<object>();
    return result;
  }

  private static Assembly FindAssemblyByName(
    CivilDocument civilDocument,
    Transaction transaction,
    string name,
    OpenMode openMode)
  {
    foreach (ObjectId id in civilDocument.AssemblyCollection)
    {
      var assembly = CivilObjectUtils.GetRequiredObject<Assembly>(transaction, id, openMode);
      if (string.Equals(assembly.Name, name, StringComparison.OrdinalIgnoreCase))
        return assembly;
    }
    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Assembly '{name}' was not found in the drawing.");
  }

  private static Subassembly FindAttachParent(
    Assembly assembly,
    Transaction transaction,
    string attachTo,
    string side,
    bool bothSides)
  {
    var candidates = bothSides ? new[] { $"{attachTo}-{side}", attachTo } : new[] { attachTo };
    var subassemblies = GetSubassemblyIds(assembly)
      .Select(id => CivilObjectUtils.GetRequiredObject<Subassembly>(transaction, id, OpenMode.ForRead))
      .ToList();
    foreach (var candidate in candidates)
    {
      var match = subassemblies.FirstOrDefault(sa => string.Equals(sa.Name, candidate, StringComparison.OrdinalIgnoreCase));
      if (match != null) return match;
    }
    throw new JsonRpcDispatchException(
      "CIVIL3D.OBJECT_NOT_FOUND",
      $"Subassembly '{string.Join("' / '", candidates)}' to attach to was not found in assembly '{assembly.Name}'.");
  }

  private static Autodesk.Civil.DatabaseServices.Point ResolveHookPoint(Subassembly parent, int? pointIndex)
  {
    var points = parent.Points;
    if (points.Count == 0)
      throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"Subassembly '{parent.Name}' has no points to attach to.");

    if (pointIndex.HasValue)
    {
      if (pointIndex.Value < 0 || pointIndex.Value >= points.Count)
        throw new JsonRpcDispatchException(
          "CIVIL3D.INVALID_INPUT",
          $"attachPointIndex {pointIndex.Value} is out of range. Subassembly '{parent.Name}' has {points.Count} point(s).");
      return points[pointIndex.Value];
    }

    // Default: outermost point, and the top one when several share that offset
    // (e.g. a lane's edge of travelway above its pavement-bottom points).
    var all = Enumerable.Range(0, points.Count).Select(i => points[i]).ToList();
    return all
      .OrderByDescending(p => Math.Round(Math.Abs(p.Offset), 6))
      .ThenByDescending(p => p.Elevation)
      .First();
  }

  private static List<ObjectId> GetSubassemblyIds(Assembly assembly)
  {
    return assembly.Groups
      .SelectMany(group => group.GetSubassemblyIds().Cast<ObjectId>())
      .Where(id => !id.IsNull)
      .Distinct()
      .ToList();
  }

  private static List<string> ApplySubassemblyParameters(AcDbObject subassembly, IReadOnlyDictionary<string, object?> parameters)
  {
    var updated = new List<string>();
    foreach (var pair in parameters)
    {
      var value = UnwrapJsonValue(pair.Value);
      // Stock subassembly inputs (Width, Slope, ...) live in the Params* collections,
      // not as .NET properties; properties are only the fallback (e.g. Side).
      if ((subassembly is Subassembly sa && TrySetStockParameter(sa, pair.Key, value))
        || Civil3DCompatibility.TrySetProperty(subassembly, pair.Key, value))
        updated.Add(pair.Key);
    }
    return updated;
  }

  private static bool TrySetStockParameter(Subassembly subassembly, string key, object? value)
  {
    if (value == null) return false;
    try
    {
      foreach (var param in subassembly.ParamsDouble)
        if (string.Equals(param.Key, key, StringComparison.OrdinalIgnoreCase)) { param.Value = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); return true; }
      foreach (var param in subassembly.ParamsLong)
        if (string.Equals(param.Key, key, StringComparison.OrdinalIgnoreCase)) { param.Value = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); return true; }
      foreach (var param in subassembly.ParamsBool)
        if (string.Equals(param.Key, key, StringComparison.OrdinalIgnoreCase)) { param.Value = Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture); return true; }
      foreach (var param in subassembly.ParamsString)
        if (string.Equals(param.Key, key, StringComparison.OrdinalIgnoreCase)) { param.Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture); return true; }
    }
    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Subassembly parameter '{key}' has an invalid value: {ex.Message}");
    }
    return false;
  }

  // JsonNode.GetValue<object>() yields a JsonElement, which Convert.ChangeType cannot handle.
  private static object? UnwrapJsonValue(object? value)
  {
    if (value is not System.Text.Json.JsonElement element) return value;
    return element.ValueKind switch
    {
      System.Text.Json.JsonValueKind.Number => element.GetDouble(),
      System.Text.Json.JsonValueKind.String => element.GetString(),
      System.Text.Json.JsonValueKind.True => true,
      System.Text.Json.JsonValueKind.False => false,
      _ => null,
    };
  }

  private static string? GetStyleName(Assembly assembly, Transaction transaction)
  {
    if (assembly.StyleId.IsNull) return null;
    return CivilObjectUtils.GetName(transaction.GetObject(assembly.StyleId, OpenMode.ForRead));
  }

  private static List<Dictionary<string, object?>> GetSubassemblies(Assembly assembly, Transaction transaction)
  {
    return GetSubassemblyIds(assembly)
      .Select(id => CivilObjectUtils.GetRequiredObject<AcDbObject>(transaction, id, OpenMode.ForRead))
      .Select(ToSubassemblySummary)
      .ToList();
  }

  private static Dictionary<string, object?> ToSubassemblySummary(AcDbObject subassembly)
  {
    return new Dictionary<string, object?>
    {
      ["name"] = CivilObjectUtils.GetName(subassembly) ?? subassembly.Handle.ToString(),
      ["handle"] = CivilObjectUtils.GetHandle(subassembly),
      ["type"] = subassembly.GetType().Name,
      ["className"] = subassembly.GetType().Name,
      ["side"] = Civil3DCompatibility.GetPropertyValue(subassembly, "Side")?.ToString()?.ToLowerInvariant() ?? "none",
      ["parameters"] = Civil3DCompatibility.GetReadableScalarProperties(subassembly),
      ["points"] = subassembly is Subassembly sa ? ReadSubassemblyPoints(sa) : new List<Dictionary<string, object?>>(),
    };
  }

  // Offsets/elevations are in the assembly layout (relative to the assembly baseline point);
  // they let the caller pick attachPointIndex instead of relying on the outermost-point default.
  private static List<Dictionary<string, object?>> ReadSubassemblyPoints(Subassembly subassembly)
  {
    var points = subassembly.Points;
    return Enumerable.Range(0, points.Count)
      .Select(i => points[i])
      .Select(p => new Dictionary<string, object?>
      {
        ["index"] = p.Index,
        ["offset"] = Math.Round(p.Offset, 4),
        ["elevation"] = Math.Round(p.Elevation, 4),
        ["codes"] = p.Codes.ToList(),
      })
      .ToList();
  }

  private static Dictionary<string, object?> ToAssemblySummary(Assembly assembly)
  {
    return new Dictionary<string, object?>
    {
      ["name"] = assembly.Name,
      ["handle"] = CivilObjectUtils.GetHandle(assembly),
      ["subassemblyCount"] = GetSubassemblyIds(assembly).Count,
      ["type"] = assembly.Type.ToString(),
    };
  }
}
