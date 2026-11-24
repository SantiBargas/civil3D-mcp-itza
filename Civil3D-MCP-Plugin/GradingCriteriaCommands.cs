using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices.Styles;

namespace Civil3DMcpPlugin;

// ITZA addition (11-20): grading criteria sets and criteria through the managed API
// (StylesRoot.GradingCriteriaSets, GradingCriteriaSet.AddCriteria, GradingCriteria properties).
// Members checked against AeccDbMgd 2026 and 2027 metadata.
public static class GradingCriteriaCommands
{
  private static readonly Dictionary<string, GradingTargetType> Targets = new(StringComparer.OrdinalIgnoreCase)
  {
    ["surface"] = GradingTargetType.Surface,
    ["elevation"] = GradingTargetType.Elevation,
    ["relative_elevation"] = GradingTargetType.RelativeElevation,
    ["distance"] = GradingTargetType.Distance,
  };

  public static Task<object?> CreateGradingCriteriaSetAsync(JsonObject? parameters)
  {
    var name = PluginRuntime.GetRequiredString(parameters, "name");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      if (FindSet(civilDoc, transaction, name, OpenMode.ForRead) != null)
      {
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Grading criteria set '{name}' already exists.");
      }

      var setId = civilDoc.Styles.GradingCriteriaSets.Add(name);
      var set = (GradingCriteriaSet)transaction.GetObject(setId, OpenMode.ForRead);
      return new Dictionary<string, object?>
      {
        ["name"] = set.Name,
        ["handle"] = CivilObjectUtils.GetHandle(set),
        ["criteriaCount"] = set.Count,
        ["created"] = true,
      };
    });
  }

  public static Task<object?> CreateGradingCriteriaAsync(JsonObject? parameters)
  {
    var setName = PluginRuntime.GetRequiredString(parameters, "setName");
    var name = PluginRuntime.GetRequiredString(parameters, "name");
    var targetText = PluginRuntime.GetRequiredString(parameters, "target");
    var projection = PluginRuntime.GetOptionalString(parameters, "projection");
    var slopeFormat = PluginRuntime.GetOptionalString(parameters, "slopeFormat");
    var searchOrder = PluginRuntime.GetOptionalString(parameters, "searchOrder");
    var overlap = PluginRuntime.GetOptionalString(parameters, "interiorCornerOverlap");
    var cutSlope = PluginRuntime.GetOptionalDouble(parameters, "cutSlope");
    var fillSlope = PluginRuntime.GetOptionalDouble(parameters, "fillSlope");
    var slope = PluginRuntime.GetOptionalDouble(parameters, "slope");
    var elevation = PluginRuntime.GetOptionalDouble(parameters, "elevation");
    var relativeElevation = PluginRuntime.GetOptionalDouble(parameters, "relativeElevation");
    var distance = PluginRuntime.GetOptionalDouble(parameters, "distance");
    var projectionDistance = PluginRuntime.GetOptionalDouble(parameters, "projectionDistance");
    var projectionRelativeElevation = PluginRuntime.GetOptionalDouble(parameters, "projectionRelativeElevation");

    if (!Targets.TryGetValue(targetText, out var target))
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"target must be one of: {string.Join(", ", Targets.Keys)}.");
    }

    GradingSlopeFormatType? format = slopeFormat == null
      ? null
      : ParseEnum<GradingSlopeFormatType>(slopeFormat, "slopeFormat");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var set = FindSet(civilDoc, transaction, setName, OpenMode.ForWrite)
        ?? throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Grading criteria set '{setName}' was not found.");

      set.AddCriteria(name);
      var criteriaId = set[name];
      var criteria = (GradingCriteria)transaction.GetObject(criteriaId, OpenMode.ForWrite);
      criteria.Target = target;

      switch (target)
      {
        case GradingTargetType.Surface:
          if (projection != null) criteria.SurfaceProjectionType = ParseEnum<GradingSurfaceProjectionType>(projection, "projection");
          break;
        case GradingTargetType.Elevation:
          if (projection != null) criteria.ElevationProjectionType = ParseEnum<GradingElevationProjectionType>(projection, "projection");
          break;
        case GradingTargetType.RelativeElevation:
          if (projection != null) criteria.RelativeElevationProjectionType = ParseEnum<GradingRelativeElevationProjectionType>(projection, "projection");
          break;
        case GradingTargetType.Distance:
          if (projection != null) criteria.DistanceProjectionType = ParseEnum<GradingDistanceProjectionType>(projection, "projection");
          break;
      }

      if (elevation.HasValue) criteria.Elevation.Value = elevation.Value;
      if (relativeElevation.HasValue) criteria.RelativeElevation.Value = relativeElevation.Value;
      if (distance.HasValue) criteria.Distance.Value = distance.Value;
      if (projectionDistance.HasValue) criteria.ProjectionDistance.Value = projectionDistance.Value;
      if (projectionRelativeElevation.HasValue) criteria.ProjectionRelativeElevation.Value = projectionRelativeElevation.Value;
      if (cutSlope.HasValue)
      {
        criteria.CutSlope.Value = cutSlope.Value;
        if (format.HasValue) criteria.CutSlopeFormatType.Value = format.Value;
      }
      if (fillSlope.HasValue)
      {
        criteria.FillSlope.Value = fillSlope.Value;
        if (format.HasValue) criteria.FillSlopeFormatType.Value = format.Value;
      }
      if (slope.HasValue)
      {
        criteria.Slope.Value = slope.Value;
        if (format.HasValue) criteria.SlopeFormatType.Value = format.Value;
      }
      if (searchOrder != null) criteria.SearchOrder.Value = ParseEnum<GradingSearchOrderType>(searchOrder, "searchOrder");
      if (overlap != null) criteria.InteriorCornerOverlap.Value = ParseEnum<GradingInteriorCornerOverlapSolutionType>(overlap, "interiorCornerOverlap");

      return new Dictionary<string, object?>
      {
        ["setName"] = set.Name,
        ["name"] = criteria.Name,
        ["handle"] = CivilObjectUtils.GetHandle(criteria),
        ["target"] = criteria.Target.ToString(),
        ["values"] = new Dictionary<string, object?>
        {
          ["surfaceProjection"] = Safe(() => criteria.SurfaceProjectionType.ToString(), target == GradingTargetType.Surface),
          ["elevationProjection"] = Safe(() => criteria.ElevationProjectionType.ToString(), target == GradingTargetType.Elevation),
          ["relativeElevationProjection"] = Safe(() => criteria.RelativeElevationProjectionType.ToString(), target == GradingTargetType.RelativeElevation),
          ["distanceProjection"] = Safe(() => criteria.DistanceProjectionType.ToString(), target == GradingTargetType.Distance),
          ["elevation"] = Safe(() => criteria.Elevation.Value, elevation.HasValue),
          ["relativeElevation"] = Safe(() => criteria.RelativeElevation.Value, relativeElevation.HasValue),
          ["distance"] = Safe(() => criteria.Distance.Value, distance.HasValue),
          ["projectionDistance"] = Safe(() => criteria.ProjectionDistance.Value, projectionDistance.HasValue),
          ["projectionRelativeElevation"] = Safe(() => criteria.ProjectionRelativeElevation.Value, projectionRelativeElevation.HasValue),
          ["cutSlope"] = Safe(() => criteria.CutSlope.Value, cutSlope.HasValue),
          ["cutSlopeFormat"] = Safe(() => criteria.CutSlopeFormatType.Value.ToString(), cutSlope.HasValue),
          ["fillSlope"] = Safe(() => criteria.FillSlope.Value, fillSlope.HasValue),
          ["fillSlopeFormat"] = Safe(() => criteria.FillSlopeFormatType.Value.ToString(), fillSlope.HasValue),
          ["slope"] = Safe(() => criteria.Slope.Value, slope.HasValue),
          ["slopeFormat"] = Safe(() => criteria.SlopeFormatType.Value.ToString(), slope.HasValue),
          ["searchOrder"] = Safe(() => criteria.SearchOrder.Value.ToString(), searchOrder != null),
          ["interiorCornerOverlap"] = Safe(() => criteria.InteriorCornerOverlap.Value.ToString(), overlap != null),
        },
        ["created"] = true,
      };
    });
  }

  private static GradingCriteriaSet? FindSet(
    Autodesk.Civil.ApplicationServices.CivilDocument civilDoc,
    Transaction transaction,
    string name,
    OpenMode mode)
  {
    foreach (ObjectId id in civilDoc.Styles.GradingCriteriaSets)
    {
      var set = (GradingCriteriaSet)transaction.GetObject(id, OpenMode.ForRead);
      if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        return mode == OpenMode.ForWrite ? (GradingCriteriaSet)transaction.GetObject(id, OpenMode.ForWrite) : set;
      }
    }

    return null;
  }

  // snake_case / camelCase / PascalCase all map to the enum member name.
  private static T ParseEnum<T>(string text, string parameter) where T : struct, Enum
  {
    var normalized = text.Replace("_", string.Empty).Replace("-", string.Empty);
    if (Enum.TryParse<T>(normalized, ignoreCase: true, out var value) && Enum.IsDefined(value))
    {
      return value;
    }

    var allowed = string.Join(", ", Enum.GetNames<T>());
    throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"'{text}' is not valid for {parameter}. Allowed (any case, underscores optional): {allowed}.");
  }

  // 2027 throws CivilException when an attribute does not apply to the criteria's target; report null instead.
  private static object? Safe<T>(Func<T> read, bool requested)
  {
    if (!requested) return null;
    try { return read(); } catch (Exception) { return null; }
  }
}
