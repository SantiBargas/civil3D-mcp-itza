using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpPlugin;

/// <summary>
/// Hydraulic analysis commands for gravity pipe networks.
/// Implements Manning's equation, HGL backwater analysis, and structure property queries.
/// Units follow the drawing: metric drawings use m, m³/s and m/s; imperial drawings use ft, cfs and ft/s.
/// </summary>
public static class PipeHydraulicsCommands
{
  // ─── calculatePipeNetworkHgl ────────────────────────────────────────────────

  public static Task<object?> CalculatePipeNetworkHglAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var tailwaterElevation = PluginRuntime.GetOptionalDouble(parameters, "tailwaterElevation");
    var designFlow = PluginRuntime.GetOptionalDouble(parameters, "designFlow");
    var manningsN = PluginRuntime.GetOptionalDouble(parameters, "manningsN") ?? 0.013;
    var perPipeFlows = ReadPerPipeFlows(parameters);

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var units = HydraulicUnits.For(database);
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName);
      var pipes = GetPipeData(network, transaction);

      if (pipes.Count == 0)
      {
        return new Dictionary<string, object?>
        {
          ["networkName"] = networkName,
          ["nodeCount"] = 0,
          ["nodes"] = Array.Empty<object>(),
          ["warning"] = "Network contains no pipes.",
        };
      }

      // Pipes run start → end in flow direction. Outlets are pipes whose end structure feeds no other pipe.
      var startsAt = pipes.Where(p => !p.StartStructureId.IsNull).ToLookup(p => p.StartStructureId);
      var endsAt = pipes.Where(p => !p.EndStructureId.IsNull).ToLookup(p => p.EndStructureId);
      var outlets = pipes.Where(p => p.EndStructureId.IsNull || !startsAt.Contains(p.EndStructureId)).ToList();

      var nodeHgl = new Dictionary<ObjectId, double>();
      var visited = new HashSet<ObjectId>();
      var nodes = new List<Dictionary<string, object?>>();
      var queue = new Queue<PipeInfo>(outlets);

      while (queue.Count > 0)
      {
        var pipe = queue.Dequeue();
        if (!visited.Add(pipe.Id)) continue;

        var flow = ResolveFlow(pipe, perPipeFlows, designFlow);
        var qFull = FullFlowCapacity(pipe, manningsN, units);
        var normalDepth = flow.HasValue ? NormalDepth(flow.Value, pipe, manningsN, units) : pipe.Diameter;
        var frictionSlope = flow.HasValue ? FullFlowFrictionSlope(flow.Value, pipe, manningsN, units) : 0.0;

        var downstreamBoundary = pipe.EndStructureId.IsNull || !nodeHgl.TryGetValue(pipe.EndStructureId, out var endHgl)
          ? tailwaterElevation ?? double.NegativeInfinity
          : endHgl;
        var hglDownstream = Math.Max(downstreamBoundary, pipe.InvertOut + normalDepth);
        var surchargedAtOutlet = hglDownstream > pipe.InvertOut + pipe.Diameter;
        var flowExceedsCapacity = flow.HasValue && flow.Value > qFull;
        var hglUpstream = surchargedAtOutlet || flowExceedsCapacity
          ? hglDownstream + frictionSlope * pipe.Length
          : Math.Max(pipe.InvertIn + normalDepth, hglDownstream + frictionSlope * pipe.Length);
        var surcharged = surchargedAtOutlet || flowExceedsCapacity || hglUpstream > pipe.InvertIn + pipe.Diameter;

        nodes.Add(new Dictionary<string, object?>
        {
          ["pipeName"] = pipe.Name,
          ["startStructure"] = pipe.StartStructureName,
          ["endStructure"] = pipe.EndStructureName,
          ["invertIn"] = Math.Round(pipe.InvertIn, 3),
          ["invertOut"] = Math.Round(pipe.InvertOut, 3),
          ["diameter"] = Math.Round(pipe.Diameter, 3),
          ["length"] = Math.Round(pipe.Length, 2),
          ["slopePct"] = Math.Round(pipe.SlopePct, 3),
          ["designFlow"] = flow.HasValue ? Math.Round(flow.Value, 4) : null,
          ["qFull"] = Math.Round(qFull, 4),
          ["normalDepth"] = flow.HasValue ? Math.Round(normalDepth, 3) : null,
          ["hglDownstream"] = Math.Round(hglDownstream, 3),
          ["hglUpstream"] = Math.Round(hglUpstream, 3),
          ["surcharged"] = surcharged,
          ["status"] = surcharged ? "SURCHARGED" : "FREE_SURFACE",
        });

        if (!pipe.StartStructureId.IsNull)
        {
          nodeHgl[pipe.StartStructureId] = nodeHgl.TryGetValue(pipe.StartStructureId, out var existing)
            ? Math.Max(existing, hglUpstream)
            : hglUpstream;
          foreach (var upstream in endsAt[pipe.StartStructureId])
            queue.Enqueue(upstream);
        }
      }

      return new Dictionary<string, object?>
      {
        ["networkName"] = networkName,
        ["units"] = units.Describe(),
        ["method"] = "Normal depth per pipe (Manning) with full-flow friction slope carried upstream; structure losses ignored.",
        ["tailwaterElevation"] = tailwaterElevation,
        ["manningsN"] = manningsN,
        ["designFlow"] = designFlow,
        ["pipeCount"] = pipes.Count,
        ["outlets"] = outlets.Select(p => p.Name).ToList(),
        ["unreachedPipes"] = pipes.Where(p => !visited.Contains(p.Id)).Select(p => p.Name).ToList(),
        ["surchargedCount"] = nodes.Count(n => (bool)(n["surcharged"] ?? false)),
        ["nodes"] = nodes,
      };
    });
  }

  // ─── analyzePipeNetworkHydraulics ──────────────────────────────────────────

  public static Task<object?> AnalyzePipeNetworkHydraulicsAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var designFlow = PluginRuntime.GetOptionalDouble(parameters, "designFlow");
    var manningsN = PluginRuntime.GetOptionalDouble(parameters, "manningsN") ?? 0.013;
    var minCoverDepthParam = PluginRuntime.GetOptionalDouble(parameters, "minCoverDepth");
    var minVelocityParam = PluginRuntime.GetOptionalDouble(parameters, "minVelocity");
    var maxVelocityParam = PluginRuntime.GetOptionalDouble(parameters, "maxVelocity");
    var minSlopeParam = PluginRuntime.GetOptionalDouble(parameters, "minSlope");
    var perPipeFlows = ReadPerPipeFlows(parameters);

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var units = HydraulicUnits.For(database);
      var minCoverDepth = minCoverDepthParam ?? units.DefaultMinCover;
      var minVelocity = minVelocityParam ?? units.DefaultMinVelocity;
      var maxVelocity = maxVelocityParam ?? units.DefaultMaxVelocity;
      var minSlope = minSlopeParam ?? units.DefaultMinSlopePct;

      var network = FindPipeNetworkByName(civilDoc, transaction, networkName);
      var pipes = GetPipeData(network, transaction);
      var structures = GetStructureData(network, transaction);

      var results = new List<Dictionary<string, object?>>();
      var violations = new List<Dictionary<string, object?>>();

      foreach (var pipe in pipes)
      {
        var qFull = FullFlowCapacity(pipe, manningsN, units);
        var area = Math.PI * pipe.Diameter * pipe.Diameter / 4.0;
        var flow = ResolveFlow(pipe, perPipeFlows, designFlow) ?? qFull;
        var capacityRatio = qFull > 0 ? flow / qFull : 0;
        var normalDepth = NormalDepth(flow, pipe, manningsN, units);
        var velocity = capacityRatio > 1.0 ? flow / area : PartialFlowVelocity(flow, normalDepth, pipe.Diameter);

        var pipeViolations = new List<string>();
        if (pipe.SlopePct < 0) pipeViolations.Add($"adverse slope {pipe.SlopePct:F3}% (end invert above start invert)");
        else if (pipe.SlopePct < minSlope) pipeViolations.Add($"slope {pipe.SlopePct:F3}% < min {minSlope}%");
        if (velocity < minVelocity) pipeViolations.Add($"velocity {velocity:F2} < min {minVelocity} {units.Velocity}");
        if (velocity > maxVelocity) pipeViolations.Add($"velocity {velocity:F2} > max {maxVelocity} {units.Velocity}");
        if (capacityRatio > 1.0) pipeViolations.Add($"flow {flow:F3} exceeds capacity {qFull:F3} {units.Flow}");
        if (pipe.MinimumCover < minCoverDepth) pipeViolations.Add($"cover {pipe.MinimumCover:F2} < min {minCoverDepth} {units.Length}");

        results.Add(new Dictionary<string, object?>
        {
          ["pipeName"] = pipe.Name,
          ["startStructure"] = pipe.StartStructureName,
          ["endStructure"] = pipe.EndStructureName,
          ["diameter"] = Math.Round(pipe.Diameter, 3),
          ["length"] = Math.Round(pipe.Length, 2),
          ["slopePct"] = Math.Round(pipe.SlopePct, 3),
          ["invertIn"] = Math.Round(pipe.InvertIn, 3),
          ["invertOut"] = Math.Round(pipe.InvertOut, 3),
          ["minimumCover"] = Math.Round(pipe.MinimumCover, 3),
          ["qFull"] = Math.Round(qFull, 4),
          ["designFlow"] = Math.Round(flow, 4),
          ["capacityRatio"] = Math.Round(capacityRatio, 3),
          ["normalDepth"] = Math.Round(normalDepth, 3),
          ["velocity"] = Math.Round(velocity, 3),
          ["status"] = pipeViolations.Count > 0 ? "FAIL" : "OK",
          ["violations"] = pipeViolations,
        });

        if (pipeViolations.Count > 0)
        {
          violations.Add(new Dictionary<string, object?>
          {
            ["pipeName"] = pipe.Name,
            ["violations"] = pipeViolations,
          });
        }
      }

      var structureSummary = structures.Select(s => new Dictionary<string, object?>
      {
        ["name"] = s.Name,
        ["rimElevation"] = Math.Round(s.RimElevation, 3),
        ["sumpElevation"] = Math.Round(s.SumpElevation, 3),
        ["depth"] = Math.Round(s.RimElevation - s.SumpElevation, 3),
        ["connectedPipes"] = s.ConnectedPipes,
      }).ToList();

      return new Dictionary<string, object?>
      {
        ["networkName"] = networkName,
        ["units"] = units.Describe(),
        ["manningsN"] = manningsN,
        ["criteria"] = new Dictionary<string, object?>
        {
          ["minSlopePct"] = minSlope,
          ["minVelocity"] = minVelocity,
          ["maxVelocity"] = maxVelocity,
          ["minCoverDepth"] = minCoverDepth,
        },
        ["pipeCount"] = pipes.Count,
        ["passCount"] = results.Count(r => (string)(r["status"] ?? "") == "OK"),
        ["failCount"] = violations.Count,
        ["pipes"] = results,
        ["violations"] = violations,
        ["structures"] = structureSummary,
      };
    });
  }

  // ─── getPipeStructureProperties ─────────────────────────────────────────────

  public static Task<object?> GetPipeStructurePropertiesAsync(JsonObject? parameters)
  {
    var networkName = PluginRuntime.GetRequiredString(parameters, "networkName");
    var structureName = PluginRuntime.GetRequiredString(parameters, "structureName");

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var network = FindPipeNetworkByName(civilDoc, transaction, networkName);
      var structure = FindStructureByName(network, transaction, structureName);

      var connectedPipes = new List<Dictionary<string, object?>>();
      for (var index = 0; index < structure.ConnectedPipesCount; index++)
      {
        var pipeId = structure.get_ConnectedPipe(index);
        if (pipeId.IsNull) continue;
        var pipe = CivilObjectUtils.GetRequiredObject<Pipe>(transaction, pipeId, OpenMode.ForRead);
        connectedPipes.Add(new Dictionary<string, object?>
        {
          ["name"] = pipe.Name,
          ["flow"] = structure.IsConnectedPipeFlowingIn(index) ? "in" : structure.IsConnectedPipeFlowingOut(index) ? "out" : "unknown",
          ["invertElevation"] = Math.Round(structure.RimElevation - structure.get_PipeInvertDepth(index), 3),
          ["diameter"] = Math.Round(pipe.InnerDiameterOrWidth, 3),
        });
      }

      return new Dictionary<string, object?>
      {
        ["networkName"] = networkName,
        ["structureName"] = structure.Name,
        ["type"] = structure.PartType.ToString(),
        ["partSize"] = structure.PartSizeName,
        ["x"] = Math.Round(structure.Location.X, 3),
        ["y"] = Math.Round(structure.Location.Y, 3),
        ["rimElevation"] = Math.Round(structure.RimElevation, 3),
        ["sumpElevation"] = Math.Round(structure.SumpElevation, 3),
        ["sumpDepth"] = Math.Round(structure.SumpDepth, 3),
        ["totalDepth"] = Math.Round(structure.RimElevation - structure.SumpElevation, 3),
        ["connectedPipes"] = connectedPipes,
      };
    });
  }

  // ─── Private helpers ─────────────────────────────────────────────────────────

  private sealed record PipeInfo(
    ObjectId Id,
    string Name,
    double Diameter,
    double Length,
    double SlopePct,
    double InvertIn,
    double InvertOut,
    double MinimumCover,
    ObjectId StartStructureId,
    ObjectId EndStructureId,
    string? StartStructureName,
    string? EndStructureName);

  private sealed record StructureInfo(string Name, double RimElevation, double SumpElevation, List<string> ConnectedPipes);

  private sealed record HydraulicUnits(
    bool Metric,
    double ManningK,
    double Gravity,
    string Length,
    string Flow,
    string Velocity,
    double DefaultMinCover,
    double DefaultMinVelocity,
    double DefaultMaxVelocity,
    double DefaultMinSlopePct)
  {
    public static HydraulicUnits For(Database database) => database.Insunits == UnitsValue.Meters
      ? new HydraulicUnits(true, 1.0, 9.81, "m", "m3/s", "m/s", 1.0, 0.9, 5.0, 0.3)
      : new HydraulicUnits(false, 1.486, 32.2, "ft", "cfs", "ft/s", 2.0, 2.0, 10.0, 0.5);

    public Dictionary<string, object?> Describe() => new()
    {
      ["system"] = Metric ? "metric" : "imperial",
      ["length"] = Length,
      ["flow"] = Flow,
      ["velocity"] = Velocity,
    };
  }

  private static Dictionary<string, double> ReadPerPipeFlows(JsonObject? parameters)
  {
    var flows = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    if (PluginRuntime.GetParameter(parameters, "perPipeDesignFlows") is not JsonArray items)
      return flows;

    foreach (var item in items.OfType<JsonObject>())
    {
      var pipeName = item["pipeName"]?.GetValue<string>();
      var flow = item["designFlow"]?.GetValue<double>();
      if (!string.IsNullOrWhiteSpace(pipeName) && flow.HasValue)
        flows[pipeName!] = flow.Value;
    }
    return flows;
  }

  private static double? ResolveFlow(PipeInfo pipe, Dictionary<string, double> perPipeFlows, double? designFlow)
  {
    return perPipeFlows.TryGetValue(pipe.Name, out var flow) ? flow : designFlow;
  }

  private static double FullFlowCapacity(PipeInfo pipe, double manningsN, HydraulicUnits units)
  {
    var area = Math.PI * pipe.Diameter * pipe.Diameter / 4.0;
    var hydraulicRadius = pipe.Diameter / 4.0;
    var slope = Math.Abs(pipe.SlopePct) / 100.0;
    return units.ManningK / manningsN * area * Math.Pow(hydraulicRadius, 2.0 / 3.0) * Math.Sqrt(slope);
  }

  private static double FullFlowFrictionSlope(double flow, PipeInfo pipe, double manningsN, HydraulicUnits units)
  {
    var area = Math.PI * pipe.Diameter * pipe.Diameter / 4.0;
    var hydraulicRadius = pipe.Diameter / 4.0;
    var conveyance = units.ManningK / manningsN * area * Math.Pow(hydraulicRadius, 2.0 / 3.0);
    return conveyance > 0 ? Math.Pow(flow / conveyance, 2) : 0.0;
  }

  // Circular section, depth y: θ = 2·acos(1 − 2y/D), A = D²/8·(θ − sinθ), P = D·θ/2.
  private static (double Area, double WettedPerimeter) PartialSection(double depth, double diameter)
  {
    var ratio = Math.Clamp(1.0 - 2.0 * depth / diameter, -1.0, 1.0);
    var theta = 2.0 * Math.Acos(ratio);
    return (diameter * diameter / 8.0 * (theta - Math.Sin(theta)), diameter * theta / 2.0);
  }

  private static double NormalDepth(double flow, PipeInfo pipe, double manningsN, HydraulicUnits units)
  {
    var slope = Math.Abs(pipe.SlopePct) / 100.0;
    if (flow <= 0 || slope <= 0) return pipe.Diameter;

    double Capacity(double depth)
    {
      var (area, perimeter) = PartialSection(depth, pipe.Diameter);
      return perimeter > 0 ? units.ManningK / manningsN * area * Math.Pow(area / perimeter, 2.0 / 3.0) * Math.Sqrt(slope) : 0.0;
    }

    // Partial-flow capacity peaks near 0.94·D, so search below that and treat larger flows as full.
    var high = 0.938 * pipe.Diameter;
    if (Capacity(high) < flow) return pipe.Diameter;
    var low = 0.0;
    for (var i = 0; i < 60; i++)
    {
      var mid = (low + high) / 2.0;
      if (Capacity(mid) >= flow) high = mid;
      else low = mid;
    }
    return high;
  }

  private static double PartialFlowVelocity(double flow, double depth, double diameter)
  {
    var (area, _) = PartialSection(depth, diameter);
    return area > 0 ? flow / area : 0.0;
  }

  private static Network FindPipeNetworkByName(object civilDoc, Transaction transaction, string name)
  {
    foreach (ObjectId objectId in ((CivilDocument)civilDoc).GetPipeNetworkIds())
    {
      var network = CivilObjectUtils.GetRequiredObject<Network>(transaction, objectId, OpenMode.ForRead);
      if (string.Equals(network.Name, name, StringComparison.OrdinalIgnoreCase))
        return network;
    }

    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Pipe network '{name}' was not found.");
  }

  private static Structure FindStructureByName(Network network, Transaction transaction, string structureName)
  {
    foreach (ObjectId objectId in network.GetStructureIds())
    {
      var structure = CivilObjectUtils.GetRequiredObject<Structure>(transaction, objectId, OpenMode.ForRead);
      if (string.Equals(structure.Name, structureName, StringComparison.OrdinalIgnoreCase))
        return structure;
    }
    throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Structure '{structureName}' was not found.");
  }

  private static List<PipeInfo> GetPipeData(Network network, Transaction transaction)
  {
    var results = new List<PipeInfo>();
    foreach (ObjectId objectId in network.GetPipeIds())
    {
      var pipe = CivilObjectUtils.GetRequiredObject<Pipe>(transaction, objectId, OpenMode.ForRead);
      var diameter = pipe.InnerDiameterOrWidth;
      // StartPoint/EndPoint are on the pipe centerline; the managed API exposes no invert property.
      var invertIn = pipe.StartPoint.Z - diameter / 2.0;
      var invertOut = pipe.EndPoint.Z - diameter / 2.0;
      var length = pipe.Length2DCenterToCenter > 0 ? pipe.Length2DCenterToCenter : pipe.Length2D;
      var slopePct = length > 0 ? (invertIn - invertOut) / length * 100.0 : 0.0;
      results.Add(new PipeInfo(
        objectId,
        pipe.Name,
        diameter,
        length,
        slopePct,
        invertIn,
        invertOut,
        pipe.MinimumCover,
        pipe.StartStructureId,
        pipe.EndStructureId,
        ResolveObjectName(transaction, pipe.StartStructureId),
        ResolveObjectName(transaction, pipe.EndStructureId)));
    }
    return results;
  }

  private static List<StructureInfo> GetStructureData(Network network, Transaction transaction)
  {
    var results = new List<StructureInfo>();
    foreach (ObjectId objectId in network.GetStructureIds())
    {
      var structure = CivilObjectUtils.GetRequiredObject<Structure>(transaction, objectId, OpenMode.ForRead);
      results.Add(new StructureInfo(structure.Name, structure.RimElevation, structure.SumpElevation, structure.GetConnectedPipeNames().ToList()));
    }
    return results;
  }

  private static string? ResolveObjectName(Transaction transaction, ObjectId id)
  {
    if (id.IsNull) return null;
    try
    {
      return CivilObjectUtils.GetName(transaction.GetObject(id, OpenMode.ForRead, false, true));
    }
    catch { return null; }
  }
}
