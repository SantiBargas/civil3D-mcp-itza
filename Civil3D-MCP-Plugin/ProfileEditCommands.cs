using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Civil3DMcpPlugin;

/// <summary>
/// Editing commands for Civil 3D vertical profiles and profile views:
/// add_pvi, delete_pvi, add_curve, set_grade, get_elevation,
/// check_k_values, profile_view_create, profile_view_band_set.
/// </summary>
public static class ProfileEditCommands
{
  // ─── profileAddPvi ────────────────────────────────────────────────────────

  public static Task<object?> AddPviAsync(JsonObject? parameters)
  {
    var alignmentName = PluginRuntime.GetRequiredString(parameters, "alignmentName");
    var profileName = PluginRuntime.GetRequiredString(parameters, "profileName");
    var station = PluginRuntime.GetRequiredDouble(parameters, "station");
    var elevation = PluginRuntime.GetRequiredDouble(parameters, "elevation");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var alignment = CivilObjectUtils.FindAlignmentByName(civilDoc, transaction, alignmentName);
      var profile = CivilObjectUtils.FindProfileByName(alignment, transaction, profileName, OpenMode.ForWrite);

      profile.PVIs.AddPVI(station, elevation);

      return new Dictionary<string, object?>
      {
        ["alignmentName"] = alignment.Name,
        ["profileName"] = profile.Name,
        ["station"] = station,
        ["elevation"] = elevation,
        ["success"] = true,
      };
    });
  }

  // ─── profileDeletePvi ─────────────────────────────────────────────────────

  public static Task<object?> DeletePviAsync(JsonObject? parameters)
  {
    var alignmentName = PluginRuntime.GetRequiredString(parameters, "alignmentName");
    var profileName = PluginRuntime.GetRequiredString(parameters, "profileName");
    var station = PluginRuntime.GetRequiredDouble(parameters, "station");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var alignment = CivilObjectUtils.FindAlignmentByName(civilDoc, transaction, alignmentName);
      var profile = CivilObjectUtils.FindProfileByName(alignment, transaction, profileName, OpenMode.ForWrite);

      var targetPvi = FindPviNearStation(profile.PVIs, station)
        ?? throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"No PVI found near station {station} in profile '{profileName}'.");
      profile.PVIs.RemoveAt(targetPvi.RawStation, targetPvi.Elevation);

      return new Dictionary<string, object?>
      {
        ["alignmentName"] = alignment.Name,
        ["profileName"] = profile.Name,
        ["station"] = station,
        ["success"] = true,
      };
    });
  }

  // ─── profileAddCurve ──────────────────────────────────────────────────────

  public static Task<object?> AddCurveAsync(JsonObject? parameters)
  {
    var alignmentName = PluginRuntime.GetRequiredString(parameters, "alignmentName");
    var profileName = PluginRuntime.GetRequiredString(parameters, "profileName");
    var pviStation = PluginRuntime.GetRequiredDouble(parameters, "pviStation");
    var length = PluginRuntime.GetRequiredDouble(parameters, "length");
    var curveType = PluginRuntime.GetOptionalString(parameters, "curveType") ?? "symmetric_parabola";

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var alignment = CivilObjectUtils.FindAlignmentByName(civilDoc, transaction, alignmentName);
      var profile = CivilObjectUtils.FindProfileByName(alignment, transaction, profileName, OpenMode.ForWrite);

      if (!string.Equals(curveType, "symmetric_parabola", StringComparison.OrdinalIgnoreCase))
      {
        throw new JsonRpcDispatchException(
          "CIVIL3D.API_ERROR",
          $"Curve type '{curveType}' is not implemented. Civil 3D's typed API path currently supports symmetric_parabola only.");
      }

      var targetPvi = FindPviNearStation(profile.PVIs, pviStation);
      if (targetPvi == null)
      {
        throw new JsonRpcDispatchException(
          "CIVIL3D.OBJECT_NOT_FOUND",
          $"No PVI found near station {pviStation} in profile '{profileName}'.");
      }

      profile.Entities.AddFreeSymmetricParabolaByPVIAndCurveLength(targetPvi, length);

      return new Dictionary<string, object?>
      {
        ["alignmentName"] = alignment.Name,
        ["profileName"] = profile.Name,
        ["pviStation"] = pviStation,
        ["curveLength"] = length,
        ["curveType"] = curveType,
        ["success"] = true,
      };
    });
  }

  // ─── profileSetGrade ──────────────────────────────────────────────────────

  public static Task<object?> SetGradeAsync(JsonObject? parameters)
  {
    var alignmentName = PluginRuntime.GetRequiredString(parameters, "alignmentName");
    var profileName = PluginRuntime.GetRequiredString(parameters, "profileName");
    var entityIndex = (int)(PluginRuntime.GetRequiredDouble(parameters, "entityIndex"));
    var grade = PluginRuntime.GetRequiredDouble(parameters, "grade");

    throw new JsonRpcDispatchException(
      "CIVIL3D.API_ERROR",
      $"Cannot set grade {grade} on profile entity {entityIndex} in '{profileName}': ProfileTangent.Grade is read-only in the Civil 3D 2026 .NET API. " +
      "Edit the adjoining PVIs instead.");
  }

  // ─── profileGetElevation ──────────────────────────────────────────────────

  /// <summary>
  /// Delegates to the same underlying implementation as
  /// ProfileCommands.GetProfileElevationAsync but is exposed as a
  /// dedicated tool per JFS-10 requirements.
  /// </summary>
  public static Task<object?> GetElevationAsync(JsonObject? parameters)
  {
    return ProfileCommands.GetProfileElevationAsync(parameters);
  }

  // ─── profileCheckKValues ──────────────────────────────────────────────────

  public static Task<object?> CheckKValuesAsync(JsonObject? parameters)
  {
    var alignmentName = PluginRuntime.GetRequiredString(parameters, "alignmentName");
    var profileName = PluginRuntime.GetRequiredString(parameters, "profileName");
    var designSpeed = PluginRuntime.GetRequiredDouble(parameters, "designSpeed");

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var alignment = CivilObjectUtils.FindAlignmentByName(civilDoc, transaction, alignmentName);
      var profile = CivilObjectUtils.FindProfileByName(alignment, transaction, profileName, OpenMode.ForRead);

      var entities = CivilObjectUtils.GetPropertyValue<object>(profile, "Entities");
      if (entities == null)
      {
        throw new JsonRpcDispatchException(
          "CIVIL3D.TRANSACTION_FAILED",
          $"Profile '{profileName}' does not expose an Entities collection.");
      }

      // AASHTO minimum K values table (metric km/h → K_sag, K_crest)
      // Source: AASHTO Green Book 2011 Table 3-36 / 3-37
      var kTable = BuildAashtoKTable();
      var (kSagMin, kCrestMin) = LookupKValues(kTable, designSpeed);

      var results = new List<Dictionary<string, object?>>();
      var index = 0;
      foreach (var entity in (System.Collections.IEnumerable)entities)
      {
        var entityType = entity?.GetType().Name ?? string.Empty;
        var isCurve = entityType.ToLowerInvariant().Contains("parabola")
          || entityType.ToLowerInvariant().Contains("curve");
        if (!isCurve)
        {
          index++;
          continue;
        }

        var curveLength = CivilObjectUtils.GetPropertyValue<double?>(entity, "Length") ?? 0;
        var gradeIn = CivilObjectUtils.GetPropertyValue<double?>(entity, "GradeIn")
          ?? CivilObjectUtils.GetPropertyValue<double?>(entity, "StartGrade") ?? 0;
        var gradeOut = CivilObjectUtils.GetPropertyValue<double?>(entity, "GradeOut")
          ?? CivilObjectUtils.GetPropertyValue<double?>(entity, "EndGrade") ?? 0;
        var algebraicDiff = Math.Abs(gradeOut - gradeIn);
        var kValue = algebraicDiff > 1e-10 ? curveLength / algebraicDiff : double.PositiveInfinity;

        var isSag = gradeOut > gradeIn;
        var requiredK = isSag ? kSagMin : kCrestMin;
        var passes = kValue >= requiredK || double.IsPositiveInfinity(kValue);

        results.Add(new Dictionary<string, object?>
        {
          ["entityIndex"] = index,
          ["curveType"] = isSag ? "sag" : "crest",
          ["curveLength"] = curveLength,
          ["gradeIn"] = gradeIn,
          ["gradeOut"] = gradeOut,
          ["algebraicDifference"] = algebraicDiff,
          ["kValue"] = double.IsPositiveInfinity(kValue) ? null : (object?)kValue,
          ["requiredK"] = requiredK,
          ["passes"] = passes,
        });
        index++;
      }

      var allPass = results.All(r => (bool)(r["passes"] ?? false));
      return new Dictionary<string, object?>
      {
        ["alignmentName"] = alignment.Name,
        ["profileName"] = profile.Name,
        ["designSpeed"] = designSpeed,
        ["kSagMinimum"] = kSagMin,
        ["kCrestMinimum"] = kCrestMin,
        ["curves"] = results,
        ["allPass"] = allPass,
        ["summary"] = allPass
          ? $"All {results.Count} vertical curve(s) meet minimum K values for {designSpeed} design speed."
          : $"{results.Count(r => !(bool)(r["passes"] ?? false))} of {results.Count} curve(s) fail minimum K value requirements.",
      };
    });
  }

  // ─── profileViewCreate ────────────────────────────────────────────────────

  // Gap between the drawing and an automatically placed profile view (drawing units).
  private const double AutoPlacementMargin = 20.0;

  public static Task<object?> ProfileViewCreateAsync(JsonObject? parameters)
  {
    var alignmentName = PluginRuntime.GetRequiredString(parameters, "alignmentName");
    var profileViewName = PluginRuntime.GetRequiredString(parameters, "profileViewName");
    var insertX = PluginRuntime.GetOptionalDouble(parameters, "insertX");
    var insertY = PluginRuntime.GetOptionalDouble(parameters, "insertY");
    if (insertX.HasValue != insertY.HasValue)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Pass both insertX and insertY, or neither for automatic placement.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var alignment = CivilObjectUtils.FindAlignmentByName(civilDoc, transaction, alignmentName);
      var autoPlace = !insertX.HasValue;
      // Measured before creating the view so it is not part of the "existing drawing".
      var drawingExtents = autoPlace ? GetModelSpaceExtents(database, transaction, null) : null;
      var insertionPoint = autoPlace ? Point3d.Origin : new Point3d(insertX!.Value, insertY!.Value, 0);

      var styleId = LookupUtils.GetProfileViewStyleId(
        civilDoc, transaction, PluginRuntime.GetOptionalString(parameters, "style"));
      var bandSetId = LookupUtils.GetProfileViewBandSetId(
        civilDoc, transaction, PluginRuntime.GetOptionalString(parameters, "bandSet"));

      // Civil 3D 2026 signatures (verified in AeccDbMgd metadata): Create(alignmentId, insertPosition)
      // and Create(alignmentId, insertPosition, name, bandSetId, styleId). The old reflection probes
      // used argument orders that do not exist, so view_create always failed.
      ObjectId pvId;
      if (!styleId.IsNull && !bandSetId.IsNull)
      {
        pvId = ProfileView.Create(alignment.ObjectId, insertionPoint, profileViewName, bandSetId, styleId);
      }
      else
      {
        pvId = ProfileView.Create(alignment.ObjectId, insertionPoint);
        var created = CivilObjectUtils.GetRequiredObject<ProfileView>(transaction, pvId, OpenMode.ForWrite);
        created.Name = profileViewName;
        if (!styleId.IsNull)
          created.StyleId = styleId;
      }

      if (pvId.IsNull)
      {
        throw new JsonRpcDispatchException(
          "CIVIL3D.TRANSACTION_FAILED",
          "ProfileView.Create returned a null id.");
      }

      var profileView = CivilObjectUtils.GetRequiredObject<ProfileView>(transaction, pvId, OpenMode.ForWrite);
      if (autoPlace)
        MoveViewTo(profileView, AutoPlacementTarget(drawingExtents), database, transaction);

      var result = DescribePlacement(profileView, database, transaction);
      result["profileViewName"] = profileView.Name;
      result["handle"] = CivilObjectUtils.GetHandle(profileView);
      result["alignmentName"] = alignment.Name;
      result["placement"] = autoPlace ? "auto" : "manual";
      result["success"] = true;
      return result;
    });
  }

  // ─── profileViewMove ──────────────────────────────────────────────────────

  public static Task<object?> ProfileViewMoveAsync(JsonObject? parameters)
  {
    var profileViewName = PluginRuntime.GetRequiredString(parameters, "profileViewName");
    var insertX = PluginRuntime.GetOptionalDouble(parameters, "insertX");
    var insertY = PluginRuntime.GetOptionalDouble(parameters, "insertY");
    if (insertX.HasValue != insertY.HasValue)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Pass both insertX and insertY, or neither for automatic placement.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var found = FindProfileViewByName(civilDoc, transaction, profileViewName);
      var profileView = CivilObjectUtils.GetRequiredObject<ProfileView>(transaction, found.ObjectId, OpenMode.ForWrite);
      var autoPlace = !insertX.HasValue;
      // insertX/insertY are the lower-left corner of the whole view (grid + bands + labels).
      var target = autoPlace
        ? AutoPlacementTarget(GetModelSpaceExtents(database, transaction, profileView))
        : new Point3d(insertX!.Value, insertY!.Value, 0);
      MoveViewTo(profileView, target, database, transaction);

      var result = DescribePlacement(profileView, database, transaction);
      result["profileViewName"] = profileView.Name;
      result["placement"] = autoPlace ? "auto" : "manual";
      result["moved"] = true;
      return result;
    });
  }

  private static Extents3d? GetModelSpaceExtents(Database database, Transaction transaction, ProfileView? exclude)
  {
    Extents3d? total = null;
    foreach (var (_, extents) in EnumerateModelSpaceExtents(database, transaction, exclude))
    {
      if (total.HasValue)
      {
        var merged = total.Value;
        merged.AddExtents(extents);
        total = merged;
      }
      else
      {
        total = extents;
      }
    }
    return total;
  }

  // Excludes the given view and what belongs to it: its band label groups (separate entities that
  // span the view's width) and Profile entities, which are drawn inside profile views.
  private static IEnumerable<(AcEntity Entity, Extents3d Extents)> EnumerateModelSpaceExtents(
    Database database, Transaction transaction, ProfileView? exclude)
  {
    var blockTable = CivilObjectUtils.GetRequiredObject<BlockTable>(transaction, database.BlockTableId, OpenMode.ForRead);
    var modelSpace = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(
      transaction, blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
    foreach (ObjectId id in modelSpace)
    {
      if (exclude != null && id == exclude.ObjectId) continue;
      if (transaction.GetObject(id, OpenMode.ForRead) is not AcEntity entity) continue;
      if (exclude != null && entity is Profile) continue;
      if (exclude != null && IsBandOf(entity, exclude.ObjectId)) continue;
      if (!TryGetReliableExtents(entity, out var extents)) continue;
      yield return (entity, extents);
    }
  }

  // A Site has no geometry of its own and an empty label group (e.g. a superelevation band with no
  // data) has nothing drawn; both report placeholder extents at (0; 0) that threw the placement off.
  private static bool TryGetReliableExtents(AcEntity entity, out Extents3d extents)
  {
    extents = default;
    if (entity is Site || entity is LabelGroup { SubEntityCount: 0 })
    {
      PluginLog.Info("ProfileView", $"ignored extents of {entity.GetType().Name} {entity.Handle} (no geometry)");
      return false;
    }
    try
    {
      extents = entity.GeometricExtents;
      return true;
    }
    catch (Autodesk.AutoCAD.Runtime.Exception)
    {
      return false; // entities without geometry (empty surfaces, etc.)
    }
  }

  private static string FormatExtents(Extents3d extents) =>
    $"({extents.MinPoint.X:F3}; {extents.MinPoint.Y:F3})-({extents.MaxPoint.X:F3}; {extents.MaxPoint.Y:F3})";

  private static Point3d AutoPlacementTarget(Extents3d? drawingExtents)
  {
    PluginLog.Info("ProfileView",
      $"auto placement: drawing extents={(drawingExtents.HasValue ? FormatExtents(drawingExtents.Value) : "none")}");
    return drawingExtents.HasValue
      ? new Point3d(drawingExtents.Value.MaxPoint.X + AutoPlacementMargin, drawingExtents.Value.MinPoint.Y, 0)
      : Point3d.Origin;
  }

  // Bands with a title block stick out of the grid, so matching by width missed them.
  private static bool IsBandOf(AcEntity entity, ObjectId viewId) =>
    entity is ProfileBandLabelGroup band && band.ViewId == viewId;

  // Grid + bands: GeometricExtents of a ProfileView covers only the grid.
  private static Extents3d GetViewTotalExtents(ProfileView view, Database database, Transaction transaction)
  {
    var viewExtents = view.GeometricExtents;
    var total = viewExtents;
    var blockTable = CivilObjectUtils.GetRequiredObject<BlockTable>(transaction, database.BlockTableId, OpenMode.ForRead);
    var modelSpace = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(
      transaction, blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
    foreach (ObjectId id in modelSpace)
    {
      if (transaction.GetObject(id, OpenMode.ForRead) is not AcEntity entity) continue;
      if (!IsBandOf(entity, view.ObjectId)) continue;
      if (!TryGetReliableExtents(entity, out var extents)) continue;
      PluginLog.Info("ProfileView",
        $"[{view.Name}] band {entity.GetType().Name} {entity.Handle} {FormatExtents(extents)} subEntities={((LabelGroup)entity).SubEntityCount}");
      total.AddExtents(extents);
    }
    return total;
  }

  // TransformBy is a no-op on profile views; they move through Graph.Location (bands follow).
  private static void MoveViewTo(ProfileView view, Point3d target, Database database, Transaction transaction)
  {
    var total = GetViewTotalExtents(view, database, transaction);
    var totalMin = total.MinPoint;
    var location = view.Location;
    PluginLog.Info("ProfileView",
      $"[{view.Name}] grid={FormatExtents(view.GeometricExtents)} total={FormatExtents(total)} location={location} target={target}");
    view.Location = new Point3d(location.X + target.X - totalMin.X, location.Y + target.Y - totalMin.Y, location.Z);
  }

  private static Dictionary<string, object?> DescribePlacement(ProfileView view, Database database, Transaction transaction)
  {
    var extents = GetViewTotalExtents(view, database, transaction);
    var overlapping = EnumerateModelSpaceExtents(database, transaction, view)
      .Where(item => item.Extents.MinPoint.X < extents.MaxPoint.X && item.Extents.MaxPoint.X > extents.MinPoint.X
        && item.Extents.MinPoint.Y < extents.MaxPoint.Y && item.Extents.MaxPoint.Y > extents.MinPoint.Y)
      .Select(item => item.Entity.GetType().Name)
      .ToList();
    return new Dictionary<string, object?>
    {
      ["extents"] = new Dictionary<string, object?>
      {
        ["minX"] = Math.Round(extents.MinPoint.X, 3),
        ["minY"] = Math.Round(extents.MinPoint.Y, 3),
        ["maxX"] = Math.Round(extents.MaxPoint.X, 3),
        ["maxY"] = Math.Round(extents.MaxPoint.Y, 3),
      },
      ["overlapsExisting"] = overlapping.Count > 0,
      ["overlappingCount"] = overlapping.Count,
      ["overlappingTypes"] = overlapping.Distinct().Take(10).ToList(),
    };
  }

  // ─── profileViewBandSet ───────────────────────────────────────────────────

  public static Task<object?> ProfileViewBandSetAsync(JsonObject? parameters)
  {
    var profileViewName = PluginRuntime.GetRequiredString(parameters, "profileViewName");
    var bandSetName = PluginRuntime.GetRequiredString(parameters, "bandSetName");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var profileView = FindProfileViewByName(civilDoc, transaction, profileViewName);
      var writeView = CivilObjectUtils.GetRequiredObject<ProfileView>(
        transaction, profileView.ObjectId, OpenMode.ForWrite);

      var bandSetId = LookupUtils.GetProfileViewBandSetId(civilDoc, transaction, bandSetName);

      writeView.Bands.ImportBandSetStyle(bandSetId);

      return new Dictionary<string, object?>
      {
        ["profileViewName"] = profileView.Name,
        ["bandSetName"] = bandSetName,
        ["success"] = true,
      };
    });
  }

  // ─── Private helpers ─────────────────────────────────────────────────────

  private static ProfileView FindProfileViewByName(
    Autodesk.Civil.ApplicationServices.CivilDocument civilDoc,
    Transaction transaction,
    string name)
  {
    // Profile views live in model space; enumerate all ProfileView objects
    var database = CivilObjectUtils.GetDatabase(civilDoc);
    var blockTable = CivilObjectUtils.GetRequiredObject<BlockTable>(
      transaction, database.BlockTableId, OpenMode.ForRead);
    var modelSpace = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(
      transaction, blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

    foreach (ObjectId objectId in modelSpace)
    {
      var obj = transaction.GetObject(objectId, OpenMode.ForRead);
      if (obj is ProfileView pv
        && string.Equals(pv.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return pv;
      }
    }

    throw new JsonRpcDispatchException(
      "CIVIL3D.OBJECT_NOT_FOUND",
      $"Profile view '{name}' was not found in model space.");
  }

  private static ProfilePVI? FindPviNearStation(ProfilePVICollection pvis, double targetStation)
  {
    ProfilePVI? closest = null;
    var minDist = double.MaxValue;

    foreach (ProfilePVI pvi in pvis)
    {
      var dist = Math.Abs(pvi.RawStation - targetStation);
      if (dist < minDist)
      {
        minDist = dist;
        closest = pvi;
      }
    }

    return closest;
  }

  /// <summary>
  /// AASHTO minimum K values (metric, km/h).
  /// Returns (K_sag_min, K_crest_min).
  /// Source: AASHTO A Policy on Geometric Design of Highways and Streets, 2011.
  /// </summary>
  private static List<(double speed, double kSag, double kCrest)> BuildAashtoKTable() =>
  [
    (30, 3, 1),
    (40, 7, 2),
    (50, 9, 4),
    (60, 11, 6),
    (70, 14, 10),
    (80, 19, 17),
    (90, 24, 29),
    (100, 30, 44),
    (110, 37, 60),
    (120, 46, 84),
    (130, 57, 114),
  ];

  private static (double kSag, double kCrest) LookupKValues(
    List<(double speed, double kSag, double kCrest)> table,
    double designSpeed)
  {
    // Find exact match first
    var exact = table.FirstOrDefault(t => Math.Abs(t.speed - designSpeed) < 0.5);
    if (exact != default)
    {
      return (exact.kSag, exact.kCrest);
    }

    // Interpolate between nearest values
    var lower = table.LastOrDefault(t => t.speed <= designSpeed);
    var upper = table.FirstOrDefault(t => t.speed > designSpeed);

    if (lower == default)
    {
      return (table[0].kSag, table[0].kCrest);
    }

    if (upper == default)
    {
      return (table[^1].kSag, table[^1].kCrest);
    }

    var ratio = (designSpeed - lower.speed) / (upper.speed - lower.speed);
    return (
      lower.kSag + ratio * (upper.kSag - lower.kSag),
      lower.kCrest + ratio * (upper.kCrest - lower.kCrest));
  }
}
