using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using CivilAssembly = Autodesk.Civil.DatabaseServices.Assembly;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcpPlugin;

/// <summary>
/// Handlers for civil3d_corridor_editing tools:
///   getCorridorTargetMappings / setCorridorTargetMappings /
///   addCorridorRegion / deleteCorridorRegion.
///
/// Civil 3D API notes:
///   Corridor.Baselines  → Baseline collection
///   Baseline.BaselineRegions → BaselineRegion collection
///   BaselineRegion.AppliedAssemblyId, StartStation, EndStation
///   Target mappings are on SubassemblyTargetInfo objects attached to each region.
/// </summary>
public static class CorridorEditingCommands
{

  // -------------------------------------------------------------------------
  // getCorridorTargetMappings
  // -------------------------------------------------------------------------

  public static Task<object?> GetCorridorTargetMappingsAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var regionIndex = PluginRuntime.GetOptionalInt(parameters, "regionIndex");
    var baselineIndex = PluginRuntime.GetOptionalInt(parameters, "baselineIndex") ?? 0;

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForRead);
      var baseline = GetBaseline(corridor, baselineIndex);

      var results = new List<Dictionary<string, object?>>();

      var regions = baseline.BaselineRegions;
      for (var ri = 0; ri < regions.Count; ri++)
      {
        if (regionIndex.HasValue && ri != regionIndex.Value) continue;
        var region = regions[ri];
        var targets = ReadTargetMappings(region, transaction);
        results.Add(new Dictionary<string, object?>
        {
          ["regionIndex"] = ri,
          ["regionName"] = region.Name,
          ["startStation"] = region.StartStation,
          ["endStation"] = region.EndStation,
          ["targets"] = targets,
        });
      }

      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["baselineIndex"] = baselineIndex,
        ["regions"] = results,
      };
    });
  }

  // -------------------------------------------------------------------------
  // setCorridorTargetMappings
  // -------------------------------------------------------------------------

  public static Task<object?> SetCorridorTargetMappingsAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var regionIndex = PluginRuntime.GetOptionalInt(parameters, "regionIndex") ?? 0;
    var baselineIndex = PluginRuntime.GetOptionalInt(parameters, "baselineIndex") ?? 0;
    var targetsNode = PluginRuntime.GetParameter(parameters, "targets") as JsonArray
      ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "targets array is required.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForWrite);
      var baseline = GetBaseline(corridor, baselineIndex);

      if (regionIndex < 0 || regionIndex >= baseline.BaselineRegions.Count)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"Region index {regionIndex} is out of range. Corridor '{corridorName}' baseline {baselineIndex} has {baseline.BaselineRegions.Count} region(s).");

      var region = baseline.BaselineRegions[regionIndex];
      var regionTargets = region.GetTargets();
      var appliedCount = 0;

      foreach (var targetNode in targetsNode)
      {
        if (targetNode is not JsonObject t) continue;
        var paramName = t["parameterName"]?.GetValue<string>();
        var targetType = t["targetType"]?.GetValue<string>();
        var targetName = t["targetName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(paramName) || string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(targetName)) continue;

        // Find the Civil 3D object to use as target
        var targetId = ResolveTargetObjectId(civilDoc, transaction, targetType!, targetName!);
        if (targetId == null)
          throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND",
            $"Target object '{targetName}' of type '{targetType}' was not found.");

        var matched = ApplyTargetMapping(regionTargets, paramName!, targetId.Value);
        if (matched == 0)
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
            $"No target named '{paramName}' in region {regionIndex}. Available: {string.Join(", ", DescribeTargets(regionTargets))}.");
        appliedCount += matched;
      }

      region.SetTargets(regionTargets);
      RebuildCorridorAndSurfaces(corridor, transaction);

      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["baselineIndex"] = baselineIndex,
        ["regionIndex"] = regionIndex,
        ["targetsApplied"] = appliedCount,
        ["message"] = $"Applied {appliedCount} target mapping(s) and rebuilt corridor '{corridorName}'.",
      };
    });
  }

  // -------------------------------------------------------------------------
  // addCorridorRegion
  // -------------------------------------------------------------------------

  public static Task<object?> AddCorridorRegionAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var baselineIndex = PluginRuntime.GetOptionalInt(parameters, "baselineIndex") ?? 0;
    var assemblyName = PluginRuntime.GetRequiredString(parameters, "assemblyName");
    var startStation = PluginRuntime.GetRequiredDouble(parameters, "startStation");
    var endStation = PluginRuntime.GetRequiredDouble(parameters, "endStation");
    var frequency = PluginRuntime.GetOptionalDouble(parameters, "frequency") ?? 10.0;
    // Civil 3D 2026 has no separate knee-point frequency; frequencyAtKneePoints is accepted but ignored.
    var frequencyAtCurves = PluginRuntime.GetOptionalDouble(parameters, "frequencyAtCurves") ?? frequency;
    var targetSurfaceName = PluginRuntime.GetOptionalString(parameters, "targetSurface");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForWrite);
      var baseline = GetBaseline(corridor, baselineIndex);
      var assemblyId = FindAssemblyId(civilDoc, transaction, assemblyName);
      ObjectId? targetSurfaceId = string.IsNullOrWhiteSpace(targetSurfaceName)
        ? null
        : CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, targetSurfaceName!, OpenMode.ForRead).ObjectId;

      // Baseline has no AddRegion in Civil 3D 2026; regions are added through BaselineRegions.
      var newRegionIndex = baseline.BaselineRegions.Count;
      var regionName = $"{assemblyName} {startStation:F2}-{endStation:F2}";
      var region = baseline.BaselineRegions.Add(regionName, assemblyId, startStation, endStation);
      ApplyRegionFrequency(region, frequency, frequencyAtCurves);
      var surfaceTargetsAssigned = targetSurfaceId.HasValue ? AssignSurfaceTargets(region, targetSurfaceId.Value) : 0;

      RebuildCorridorAndSurfaces(corridor, transaction);

      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["baselineIndex"] = baselineIndex,
        ["regionIndex"] = newRegionIndex,
        ["surfaceTargetsAssigned"] = surfaceTargetsAssigned,
        ["regionName"] = region.Name,
        ["assemblyName"] = assemblyName,
        ["startStation"] = startStation,
        ["endStation"] = endStation,
        ["frequency"] = frequency,
        ["message"] = $"Region added to corridor '{corridorName}' at stations {startStation:F3}–{endStation:F3}.",
      };
    });
  }

  // -------------------------------------------------------------------------
  // createCorridor
  // -------------------------------------------------------------------------

  public static Task<object?> CreateCorridorAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var alignmentName = PluginRuntime.GetRequiredString(parameters, "alignmentName");
    var profileName = PluginRuntime.GetRequiredString(parameters, "profileName");
    var assemblyName = PluginRuntime.GetRequiredString(parameters, "assemblyName");
    var startStation = PluginRuntime.GetOptionalDouble(parameters, "startStation");
    var endStation = PluginRuntime.GetOptionalDouble(parameters, "endStation");
    var frequency = PluginRuntime.GetOptionalDouble(parameters, "frequency") ?? 10.0;
    var frequencyAtCurves = PluginRuntime.GetOptionalDouble(parameters, "frequencyAtCurves") ?? frequency;
    var targetSurfaceName = PluginRuntime.GetOptionalString(parameters, "targetSurface");
    var surfaceRequests = ReadCorridorSurfaceRequests(PluginRuntime.GetParameter(parameters, "corridorSurfaces") as JsonArray);

    Func<Document, Autodesk.Civil.ApplicationServices.CivilDocument, Database, Transaction, object?> work = (doc, civilDoc, database, transaction) =>
    {
      foreach (ObjectId existingId in civilDoc.CorridorCollection)
      {
        var existing = CivilObjectUtils.GetRequiredObject<Corridor>(transaction, existingId, OpenMode.ForRead);
        if (string.Equals(existing.Name, corridorName, StringComparison.OrdinalIgnoreCase))
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"A corridor named '{corridorName}' already exists.");
      }

      var alignment = CivilObjectUtils.FindAlignmentByName(civilDoc, transaction, alignmentName);
      var profile = CivilObjectUtils.FindProfileByName(alignment, transaction, profileName, OpenMode.ForRead);
      var assemblyId = FindAssemblyId(civilDoc, transaction, assemblyName);

      var start = startStation ?? alignment.StartingStation;
      var end = endStation ?? alignment.EndingStation;
      if (start >= end || start < alignment.StartingStation - 1e-6 || end > alignment.EndingStation + 1e-6)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"Stations {start:F3}–{end:F3} must be increasing and within alignment '{alignmentName}' ({alignment.StartingStation:F3}–{alignment.EndingStation:F3}).");

      ObjectId? targetSurfaceId = null;
      if (!string.IsNullOrWhiteSpace(targetSurfaceName))
        targetSurfaceId = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, targetSurfaceName!, OpenMode.ForRead).ObjectId;

      var corridorId = civilDoc.CorridorCollection.Add(corridorName, alignmentName, alignment.ObjectId, profile.ObjectId);
      var corridor = CivilObjectUtils.GetRequiredObject<Corridor>(transaction, corridorId, OpenMode.ForWrite);
      var baseline = corridor.Baselines[0];
      var region = baseline.BaselineRegions.Add($"{assemblyName} {start:F2}-{end:F2}", assemblyId, start, end);
      ApplyRegionFrequency(region, frequency, frequencyAtCurves);

      var surfaceTargetsAssigned = targetSurfaceId.HasValue ? AssignSurfaceTargets(region, targetSurfaceId.Value) : 0;

      RebuildCorridorAndSurfaces(corridor, transaction);
      var createdSurfaces = AddCorridorSurfaces(corridor, surfaceRequests, transaction);

      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridor.Name,
        ["handle"] = CivilObjectUtils.GetHandle(corridor),
        ["alignmentName"] = alignmentName,
        ["profileName"] = profileName,
        ["assemblyName"] = assemblyName,
        ["regionName"] = region.Name,
        ["startStation"] = start,
        ["endStation"] = end,
        ["frequency"] = frequency,
        ["targetSurface"] = targetSurfaceName,
        ["surfaceTargetsAssigned"] = surfaceTargetsAssigned,
        ["corridorSurfaces"] = createdSurfaces,
        ["state"] = corridor.IsOutOfDate ? "out_of_date" : "built",
      };
    };

    return CivilExecution.WriteAsync(work);
  }

  // -------------------------------------------------------------------------
  // deleteCorridorRegion
  // -------------------------------------------------------------------------

  public static Task<object?> DeleteCorridorRegionAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var baselineIndex = PluginRuntime.GetOptionalInt(parameters, "baselineIndex") ?? 0;
    var regionIndex = PluginRuntime.GetRequiredInt(parameters, "regionIndex");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForWrite);
      var baseline = GetBaseline(corridor, baselineIndex);

      if (regionIndex < 0 || regionIndex >= baseline.BaselineRegions.Count)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"Region index {regionIndex} is out of range.");

      var region = baseline.BaselineRegions[regionIndex];
      var regionName = region.Name;
      var startStation = region.StartStation;
      var endStation = region.EndStation;

      // Baseline has no RemoveRegion/DeleteRegion in Civil 3D 2026; removal goes through BaselineRegions.
      baseline.BaselineRegions.RemoveAt(regionIndex);
      RebuildCorridorAndSurfaces(corridor, transaction);

      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["baselineIndex"] = baselineIndex,
        ["deletedRegionIndex"] = regionIndex,
        ["deletedRegionName"] = regionName,
        ["deletedStartStation"] = startStation,
        ["deletedEndStation"] = endStation,
        ["message"] = $"Region '{regionName}' deleted from corridor '{corridorName}'.",
      };
    });
  }

  // -------------------------------------------------------------------------
  // editCorridorRegion
  // -------------------------------------------------------------------------

  public static Task<object?> EditCorridorRegionAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var baselineIndex = PluginRuntime.GetOptionalInt(parameters, "baselineIndex") ?? 0;
    var regionIndex = PluginRuntime.GetRequiredInt(parameters, "regionIndex");
    var startStation = PluginRuntime.GetOptionalDouble(parameters, "startStation");
    var endStation = PluginRuntime.GetOptionalDouble(parameters, "endStation");
    var assemblyName = PluginRuntime.GetOptionalString(parameters, "assemblyName");
    var frequency = PluginRuntime.GetOptionalDouble(parameters, "frequency");
    var frequencyAtCurves = PluginRuntime.GetOptionalDouble(parameters, "frequencyAtCurves");
    if (startStation == null && endStation == null && string.IsNullOrWhiteSpace(assemblyName) && frequency == null && frequencyAtCurves == null)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
        "Nothing to edit: pass startStation, endStation, assemblyName, frequency or frequencyAtCurves.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForWrite);
      var baseline = GetBaseline(corridor, baselineIndex);
      if (regionIndex < 0 || regionIndex >= baseline.BaselineRegions.Count)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"Region index {regionIndex} is out of range. Corridor '{corridorName}' baseline {baselineIndex} has {baseline.BaselineRegions.Count} region(s).");

      var region = baseline.BaselineRegions[regionIndex];
      var oldAssemblyName = CivilObjectUtils.GetRequiredObject<CivilAssembly>(transaction, region.AssemblyId, OpenMode.ForRead).Name;
      var hasAutoName = region.Name == $"{oldAssemblyName} {region.StartStation:F2}-{region.EndStation:F2}";
      var newStart = startStation ?? region.StartStation;
      var newEnd = endStation ?? region.EndStation;
      if (newStart >= newEnd)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"startStation ({newStart:F3}) must be lower than endStation ({newEnd:F3}).");

      // Order matters: widening first avoids a transient start > end.
      if (newEnd > region.EndStation) { region.EndStation = newEnd; region.StartStation = newStart; }
      else { region.StartStation = newStart; region.EndStation = newEnd; }

      if (!string.IsNullOrWhiteSpace(assemblyName))
        region.AssemblyId = FindAssemblyId(civilDoc, transaction, assemblyName!);

      if (frequency != null || frequencyAtCurves != null)
      {
        var setting = region.AppliedAssemblySetting;
        var tangents = frequency ?? setting.FrequencyAlongTangents;
        ApplyRegionFrequency(region, tangents, frequencyAtCurves ?? frequency ?? setting.FrequencyAlongCurves);
      }

      var assembly = CivilObjectUtils.GetRequiredObject<CivilAssembly>(transaction, region.AssemblyId, OpenMode.ForRead);
      // Only names generated by this plugin ("<assembly> <start>-<end>") are renamed; user names are kept.
      if (hasAutoName)
        region.Name = $"{assembly.Name} {region.StartStation:F2}-{region.EndStation:F2}";

      RebuildCorridorAndSurfaces(corridor, transaction);

      var finalSetting = region.AppliedAssemblySetting;
      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["baselineIndex"] = baselineIndex,
        ["regionIndex"] = regionIndex,
        ["regionName"] = region.Name,
        ["startStation"] = region.StartStation,
        ["endStation"] = region.EndStation,
        ["assemblyName"] = assembly.Name,
        ["frequency"] = finalSetting.FrequencyAlongTangents,
        ["frequencyAtCurves"] = finalSetting.FrequencyAlongCurves,
      };
    });
  }

  // -------------------------------------------------------------------------
  // deleteCorridor
  // -------------------------------------------------------------------------

  public static Task<object?> DeleteCorridorAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForWrite);
      var surfaceNames = corridor.CorridorSurfaces.Cast<CorridorSurface>().Select(s => s.Name).ToList();
      // CorridorCollection has no Remove in Civil 3D 2026; erasing the corridor also drops its corridor surfaces.
      corridor.Erase();
      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["deletedCorridorSurfaces"] = surfaceNames,
        ["deleted"] = true,
      };
    });
  }

  // -------------------------------------------------------------------------
  // addCorridorSurfaces / deleteCorridorSurface
  // -------------------------------------------------------------------------

  public static Task<object?> AddCorridorSurfacesAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var requests = ReadCorridorSurfaceRequests(PluginRuntime.GetParameter(parameters, "corridorSurfaces") as JsonArray);
    if (requests.Count == 0)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "corridorSurfaces must contain at least one surface.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForWrite);
      if (corridor.IsOutOfDate)
        RebuildCorridorAndSurfaces(corridor, transaction);
      var created = AddCorridorSurfaces(corridor, requests, transaction);
      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["corridorSurfaces"] = created,
      };
    });
  }

  public static Task<object?> DeleteCorridorSurfaceAsync(JsonObject? parameters)
  {
    var corridorName = PluginRuntime.GetRequiredString(parameters, "corridorName");
    var surfaceName = PluginRuntime.GetRequiredString(parameters, "corridorSurface");
    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var corridor = CivilObjectUtils.FindCorridorByName(civilDoc, transaction, corridorName, OpenMode.ForWrite);
      var existing = corridor.CorridorSurfaces.Cast<CorridorSurface>()
        .FirstOrDefault(s => string.Equals(s.Name, surfaceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND",
          $"Corridor surface '{surfaceName}' was not found on corridor '{corridorName}'.");
      var deletedName = existing.Name;
      corridor.CorridorSurfaces.Remove(deletedName);
      return new Dictionary<string, object?>
      {
        ["corridorName"] = corridorName,
        ["corridorSurface"] = deletedName,
        ["deleted"] = true,
      };
    });
  }

  // -------------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------------

  // In application context Corridor.Rebuild() leaves corridor-surface TINs empty (Civil 3D
  // builds them when a command ends), so each corridor surface is rebuilt explicitly.
  internal static void RebuildCorridorAndSurfaces(Corridor corridor, Transaction transaction)
  {
    corridor.Rebuild();
    foreach (CorridorSurface corridorSurface in corridor.CorridorSurfaces)
    {
      if (corridorSurface.SurfaceId.IsNull) continue;
      var surface = CivilObjectUtils.GetRequiredObject<CivilSurface>(transaction, corridorSurface.SurfaceId, OpenMode.ForWrite);
      surface.Rebuild();
    }
  }

  private static Baseline GetBaseline(Corridor corridor, int index)
  {
    if (index < 0 || index >= corridor.Baselines.Count)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
        $"Baseline index {index} is out of range. Corridor '{corridor.Name}' has {corridor.Baselines.Count} baseline(s).");
    return corridor.Baselines[index];
  }

  private static ObjectId FindAssemblyId(
    Autodesk.Civil.ApplicationServices.CivilDocument civilDoc, Transaction transaction, string assemblyName)
  {
    foreach (ObjectId aid in civilDoc.AssemblyCollection)
    {
      var asm = CivilObjectUtils.GetRequiredObject<CivilAssembly>(transaction, aid, OpenMode.ForRead);
      if (string.Equals(asm.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
        return aid;
    }
    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Assembly '{assemblyName}' was not found.");
  }

  private static void ApplyRegionFrequency(BaselineRegion region, double frequency, double frequencyAtCurves)
  {
    var setting = region.AppliedAssemblySetting;
    setting.FrequencyAlongTangents = frequency;
    setting.FrequencyAlongCurves = frequencyAtCurves;
    setting.FrequencyAlongSpirals = frequencyAtCurves;
    setting.FrequencyAlongProfileCurves = frequencyAtCurves;
  }

  private static int AssignSurfaceTargets(BaselineRegion region, ObjectId surfaceId)
  {
    var targets = region.GetTargets();
    var assigned = 0;
    foreach (SubassemblyTargetInfo info in targets)
    {
      if (info.TargetType != SubassemblyLogicalNameType.Surface) continue;
      var ids = new ObjectIdCollection();
      ids.Add(surfaceId);
      info.TargetIds = ids;
      assigned++;
    }
    if (assigned > 0)
      region.SetTargets(targets);
    return assigned;
  }

  // Link codes (Top, Datum, ...) only exist once the corridor has been built, so the
  // corridor must already be rebuilt when this runs; it rebuilds again to build the surfaces.
  private static List<Dictionary<string, object?>> AddCorridorSurfaces(Corridor corridor, List<CorridorSurfaceRequest> requests, Transaction transaction)
  {
    var created = new List<Dictionary<string, object?>>();
    if (requests.Count == 0) return created;

    PluginLog.Info("Corridor", $"[{corridor.Name}] reading link codes");
    var availableLinkCodes = corridor.GetLinkCodes();
    PluginLog.Info("Corridor", $"[{corridor.Name}] link codes: {string.Join(", ", availableLinkCodes)}");
    var existingNames = corridor.CorridorSurfaces.Cast<CorridorSurface>().Select(s => s.Name).ToList();
    foreach (var request in requests)
    {
      if (existingNames.Contains(request.Name, StringComparer.OrdinalIgnoreCase))
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"Corridor '{corridor.Name}' already has a surface named '{request.Name}'.");
      var unknown = request.LinkCodes
        .Where(code => !availableLinkCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
        .ToList();
      if (unknown.Count > 0)
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT",
          $"Link code(s) {string.Join(", ", unknown)} are not produced by corridor '{corridor.Name}'. Available: {string.Join(", ", availableLinkCodes)}.");

      PluginLog.Info("Corridor", $"[{corridor.Name}] adding corridor surface '{request.Name}'");
      var corridorSurface = corridor.CorridorSurfaces.Add(request.Name);
      foreach (var code in request.LinkCodes)
      {
        PluginLog.Info("Corridor", $"[{corridor.Name}] adding link code '{code}' to '{request.Name}'");
        corridorSurface.AddLinkCode(code, true);
      }
      existingNames.Add(request.Name);
      created.Add(new Dictionary<string, object?>
      {
        ["name"] = request.Name,
        ["linkCodes"] = request.LinkCodes,
      });
    }

    PluginLog.Info("Corridor", $"[{corridor.Name}] rebuilding with {created.Count} new surface(s)");
    RebuildCorridorAndSurfaces(corridor, transaction);
    PluginLog.Info("Corridor", $"[{corridor.Name}] rebuild done");
    return created;
  }

  private sealed record CorridorSurfaceRequest(string Name, List<string> LinkCodes);

  private static List<CorridorSurfaceRequest> ReadCorridorSurfaceRequests(JsonArray? node)
  {
    var requests = new List<CorridorSurfaceRequest>();
    if (node == null) return requests;
    foreach (var item in node)
    {
      if (item is not JsonObject obj) continue;
      var name = obj["name"]?.GetValue<string>();
      if (string.IsNullOrWhiteSpace(name))
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Each corridorSurfaces entry needs a name.");
      var codes = (obj["linkCodes"] as JsonArray)?
        .Select(code => code?.GetValue<string>())
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Select(code => code!)
        .ToList() ?? new List<string>();
      if (codes.Count == 0) codes.Add("Top");
      requests.Add(new CorridorSurfaceRequest(name!, codes));
    }
    return requests;
  }

  private static List<Dictionary<string, object?>> ReadTargetMappings(
    BaselineRegion region, Transaction transaction)
  {
    var mappings = new List<Dictionary<string, object?>>();
    foreach (SubassemblyTargetInfo info in region.GetTargets())
    {
      var targetNames = info.TargetIds.Cast<ObjectId>()
        .Where(id => !id.IsNull)
        .Select(id => CivilObjectUtils.GetName(transaction.GetObject(id, OpenMode.ForRead)))
        .ToList();

      mappings.Add(new Dictionary<string, object?>
      {
        ["parameterName"] = info.LogicalName,
        ["displayName"] = info.DisplayName,
        ["subassemblyName"] = info.SubassemblyName,
        ["targetType"] = info.TargetType.ToString(),
        ["targetName"] = targetNames.FirstOrDefault(),
        ["targetNames"] = targetNames,
      });
    }

    return mappings;
  }

  private static ObjectId? ResolveTargetObjectId(
    Autodesk.Civil.ApplicationServices.CivilDocument civilDoc,
    Transaction transaction, string targetType, string targetName)
  {
    switch (targetType.ToLowerInvariant())
    {
      case "surface":
        foreach (ObjectId id in civilDoc.GetSurfaceIds())
        {
          var s = CivilObjectUtils.GetRequiredObject<CivilSurface>(transaction, id, OpenMode.ForRead);
          if (string.Equals(s.Name, targetName, StringComparison.OrdinalIgnoreCase)) return id;
        }
        break;
      case "alignment":
        foreach (ObjectId id in civilDoc.GetAlignmentIds())
        {
          var a = CivilObjectUtils.GetRequiredObject<Alignment>(transaction, id, OpenMode.ForRead);
          if (string.Equals(a.Name, targetName, StringComparison.OrdinalIgnoreCase)) return id;
        }
        break;
      case "profile":
        foreach (ObjectId aid in civilDoc.GetAlignmentIds())
        {
          var alignment = CivilObjectUtils.GetRequiredObject<Alignment>(transaction, aid, OpenMode.ForRead);
          foreach (ObjectId pid in alignment.GetProfileIds())
          {
            var p = CivilObjectUtils.GetRequiredObject<Profile>(transaction, pid, OpenMode.ForRead);
            if (string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase)) return pid;
          }
        }
        break;
    }
    return null;
  }

  // Matches by logical name, display name or subassembly name; a logical name such as
  // the daylight surface is shared by both sides, so one entry may update several targets.
  private static int ApplyTargetMapping(SubassemblyTargetInfoCollection targets, string paramName, ObjectId targetId)
  {
    var matched = 0;
    foreach (SubassemblyTargetInfo info in targets)
    {
      if (!string.Equals(info.LogicalName, paramName, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(info.DisplayName, paramName, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(info.SubassemblyName, paramName, StringComparison.OrdinalIgnoreCase))
        continue;
      var ids = new ObjectIdCollection();
      ids.Add(targetId);
      info.TargetIds = ids;
      matched++;
    }
    return matched;
  }

  private static IEnumerable<string> DescribeTargets(SubassemblyTargetInfoCollection targets)
  {
    foreach (SubassemblyTargetInfo info in targets)
      yield return $"{info.LogicalName} ({info.SubassemblyName}, {info.TargetType})";
  }
}
