using System.Collections;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3DMcpPlugin;

/// <summary>
/// Detention basin sizing and stage-storage commands.
/// Implements Modified Rational Method storage estimation and
/// stage-storage table generation from Civil 3D surfaces.
/// </summary>
public static class DetentionCommands
{
  // ─── calculateDetentionBasinSize ─────────────────────────────────────────────

  public static Task<object?> CalculateDetentionBasinSizeAsync(JsonObject? parameters)
  {
    var inflow = PluginRuntime.GetRequiredDouble(parameters, "inflow");
    var outflow = PluginRuntime.GetRequiredDouble(parameters, "outflow");
    var stormDuration = PluginRuntime.GetOptionalDouble(parameters, "stormDuration") ?? 60.0;
    var method = PluginRuntime.GetOptionalString(parameters, "method") ?? "modified_rational";
    var sideSlope = PluginRuntime.GetOptionalDouble(parameters, "sideSlope") ?? 3.0;
    var bottomWidth = PluginRuntime.GetOptionalDouble(parameters, "bottomWidth") ?? 10.0;
    var freeboardDepth = PluginRuntime.GetOptionalDouble(parameters, "freeboardDepth") ?? 1.0;

    if (outflow >= inflow)
    {
      return Task.FromResult<object?>(new Dictionary<string, object?>
      {
        ["error"] = "Outflow must be less than inflow for detention to be required.",
        ["inflow"] = inflow,
        ["outflow"] = outflow,
        ["storageRequired"] = false,
      });
    }

    // Modified Rational Method: Vs = (Qi - Qo) * tc * 60  [cubic feet]
    double storageVolumeCf;
    string methodLabel;

    switch (method.ToLowerInvariant())
    {
      case "triangular_hydrograph":
        // Approximate: Vs ≈ 0.5 * (Qi - Qo) * tc * 60
        storageVolumeCf = 0.5 * (inflow - outflow) * stormDuration * 60.0;
        methodLabel = "Triangular Hydrograph Approximation";
        break;
      default: // modified_rational
        storageVolumeCf = (inflow - outflow) * stormDuration * 60.0;
        methodLabel = "Modified Rational Method";
        break;
    }

    var storageVolumeAcFt = storageVolumeCf / 43560.0;
    var storageVolumeCuM = storageVolumeCf / 35.3147;

    // Basin geometry — trapezoidal cross-section, assuming L = 2 * W for plan view
    // V = d * (L*W + (L + 2*H*z) * (W + 2*H*z) + 4 * (L + H*z) * (W + H*z)) / 6  (frustum formula)
    // Simplified: iterate depth to find where volume matches
    double basinDepth = EstimateBasinDepth(storageVolumeCf, bottomWidth, sideSlope);
    double basinLength = 2.0 * bottomWidth;
    double topWidth = bottomWidth + 2.0 * sideSlope * basinDepth;
    double topLength = basinLength + 2.0 * sideSlope * basinDepth;
    double totalDepth = basinDepth + freeboardDepth;

    // Orifice sizing: Q = Cd * A * sqrt(2*g*h)  →  A = Q / (Cd * sqrt(2*g*h))
    double cd = 0.6;
    double g = 32.2; // ft/s²
    double headH = basinDepth; // design head = full basin depth
    double orificeArea = outflow / (cd * Math.Sqrt(2.0 * g * headH));
    double orificeDiameterFt = Math.Sqrt(4.0 * orificeArea / Math.PI);
    double orificeDiameterIn = orificeDiameterFt * 12.0;

    return Task.FromResult<object?>(new Dictionary<string, object?>
    {
      ["method"] = methodLabel,
      ["inflow"] = inflow,
      ["outflow"] = outflow,
      ["stormDurationMinutes"] = stormDuration,
      ["requiredStorageCubicFeet"] = Math.Round(storageVolumeCf, 0),
      ["requiredStorageAcreFeet"] = Math.Round(storageVolumeAcFt, 3),
      ["requiredStorageCubicMeters"] = Math.Round(storageVolumeCuM, 1),
      ["basinGeometry"] = new Dictionary<string, object?>
      {
        ["bottomWidth"] = Math.Round(bottomWidth, 1),
        ["bottomLength"] = Math.Round(basinLength, 1),
        ["sideSlope"] = $"{sideSlope}:1",
        ["designDepth"] = Math.Round(basinDepth, 2),
        ["freeboardDepth"] = Math.Round(freeboardDepth, 2),
        ["totalDepth"] = Math.Round(totalDepth, 2),
        ["topWidth"] = Math.Round(topWidth, 1),
        ["topLength"] = Math.Round(topLength, 1),
      },
      ["outletOrifice"] = new Dictionary<string, object?>
      {
        ["designFlow"] = outflow,
        ["dischargeCoefficient"] = cd,
        ["designHead"] = Math.Round(headH, 2),
        ["requiredAreaSqFt"] = Math.Round(orificeArea, 4),
        ["requiredDiameterInches"] = Math.Round(orificeDiameterIn, 2),
        ["recommendedDiameterInches"] = Math.Round(Math.Ceiling(orificeDiameterIn * 2) / 2.0, 1),
      },
      ["notes"] = new[]
      {
        "Storage volume uses simplified Modified Rational Method — confirm with routing software for permit submittals.",
        "Basin dimensions assume trapezoidal cross-section with L = 2W plan ratio.",
        "Orifice size based on full-depth head — verify with stage-discharge routing.",
        "Add freeboard to design depth before specifying construction drawings.",
      },
    });
  }

  // ─── calculateDetentionStageStorage ──────────────────────────────────────────

  // ─── calculateDetentionStageStorage ──────────────────

  // Exact plan area and volume from the TIN: each triangle is clipped against the plane z = elevation,
  // so no Autodesk "area at elevation" API is needed. Per triangle the flooded area A(z) is piecewise
  // quadratic, so Simpson on each piece integrates it exactly.
  public static Task<object?> CalculateDetentionStageStorageAsync(JsonObject? parameters)
  {
    var surfaceName = PluginRuntime.GetRequiredString(parameters, "surfaceName");
    var bottomElevation = PluginRuntime.GetRequiredDouble(parameters, "bottomElevation");
    var topElevation = PluginRuntime.GetRequiredDouble(parameters, "topElevation");
    var elevIncrement = PluginRuntime.GetOptionalDouble(parameters, "elevationIncrement") ?? 0.5;
    var outletType = PluginRuntime.GetOptionalString(parameters, "outletType") ?? "orifice";
    var outletDiameter = PluginRuntime.GetOptionalDouble(parameters, "outletDiameter");
    var weirLength = PluginRuntime.GetOptionalDouble(parameters, "weirLength");
    var isWeir = string.Equals(outletType, "weir", StringComparison.OrdinalIgnoreCase);
    var cd = PluginRuntime.GetOptionalDouble(parameters, "dischargeCoefficient") ?? (isWeir ? 1.84 : 0.6);
    double? seedX = null, seedY = null;
    if (parameters?["seedPoint"] is JsonObject seed)
    {
      seedX = seed["x"]?.GetValue<double>();
      seedY = seed["y"]?.GetValue<double>();
      if (seedX == null || seedY == null)
      {
        throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "seedPoint requires numeric x and y.");
      }
    }

    if (topElevation <= bottomElevation)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "topElevation must be greater than bottomElevation.");
    }
    if (elevIncrement <= 0 || (topElevation - bottomElevation) / elevIncrement > 5000)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "elevationIncrement must be positive and give at most 5000 levels.");
    }

    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var surface = CivilObjectUtils.FindSurfaceByName(civilDoc, transaction, surfaceName, OpenMode.ForRead);
      if (surface is not TinSurface tin)
      {
        throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"Stage-storage needs a TIN surface, not '{surface.GetType().Name}'.");
      }

      var tris = new List<StageTri>();
      var vertexIndex = new Dictionary<(long, long), int>();
      int Index(double x, double y)
      {
        var key = ((long)Math.Round(x * 1e6), (long)Math.Round(y * 1e6));
        if (!vertexIndex.TryGetValue(key, out var i)) { i = vertexIndex.Count; vertexIndex[key] = i; }
        return i;
      }
      foreach (TinSurfaceTriangle t in tin.Triangles)
      {
        if (!t.IsVisible) continue;
        var p1 = t.Vertex1.Location; var p2 = t.Vertex2.Location; var p3 = t.Vertex3.Location;
        tris.Add(new StageTri(
          p1.X, p1.Y, p1.Z, p2.X, p2.Y, p2.Z, p3.X, p3.Y, p3.Z,
          Index(p1.X, p1.Y), Index(p2.X, p2.Y), Index(p3.X, p3.Y)));
      }
      if (tris.Count == 0)
      {
        throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"Surface '{surfaceName}' has no visible triangles.");
      }

      var edges = new Dictionary<(int, int), List<int>>();
      for (var i = 0; i < tris.Count; i++)
      {
        foreach (var e in tris[i].Edges())
        {
          if (!edges.TryGetValue(e, out var list)) edges[e] = list = new List<int>();
          list.Add(i);
        }
      }

      var seedTri = -1;
      if (seedX.HasValue)
      {
        for (var i = 0; i < tris.Count && seedTri < 0; i++)
        {
          if (tris[i].ContainsXY(seedX.Value, seedY!.Value)) seedTri = i;
        }
        if (seedTri < 0)
        {
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "seedPoint is outside the surface triangles.");
        }
      }

      HashSet<int>? Connected(double h)
      {
        if (seedTri < 0) return null;
        var set = new HashSet<int>();
        if (tris[seedTri].MinZ >= h) return set;
        var queue = new Queue<int>();
        set.Add(seedTri); queue.Enqueue(seedTri);
        while (queue.Count > 0)
        {
          var cur = queue.Dequeue();
          foreach (var e in tris[cur].Edges())
          {
            if (tris[cur].EdgeMinZ(e) >= h) continue;
            foreach (var n in edges[e])
            {
              if (n != cur && tris[n].MinZ < h && set.Add(n)) queue.Enqueue(n);
            }
          }
        }
        return set;
      }

      (double Area, double Volume) Measure(double h)
      {
        var set = Connected(h);
        double area = 0, volume = 0;
        for (var i = 0; i < tris.Count; i++)
        {
          if (set != null && !set.Contains(i)) continue;
          area += tris[i].AreaBelow(h);
          volume += tris[i].VolumeBelow(h);
        }
        return (area, volume);
      }

      var levels = new List<double>();
      for (var h = bottomElevation; h < topElevation - 1e-9; h += elevIncrement) levels.Add(h);
      levels.Add(topElevation);

      // SI (m, m3/s, g = 9.81). Orifice centre and weir crest sit at the basin bottom elevation.
      Func<double, double>? dischargeModel = null;
      string dischargeNote;
      if (string.Equals(outletType, "riser", StringComparison.OrdinalIgnoreCase))
      {
        dischargeNote = "Discharge not computed for outletType 'riser' (needs crest and barrel data); use 'orifice' or 'weir'.";
      }
      else if (isWeir && weirLength is > 0)
      {
        var len = weirLength.Value;
        dischargeModel = h => cd * len * Math.Pow(Math.Max(0, h - bottomElevation), 1.5);
        dischargeNote = $"discharge (m3/s) = C*L*H^1.5, C={cd}, L={len} m, H above bottomElevation (crest at bottomElevation).";
      }
      else if (!isWeir && outletDiameter is > 0)
      {
        var area = Math.PI * outletDiameter.Value * outletDiameter.Value / 4.0;
        var centre = bottomElevation + outletDiameter.Value / 2.0;
        dischargeModel = h => cd * area * Math.Sqrt(2 * 9.81 * Math.Max(0, h - centre));
        dischargeNote = $"discharge (m3/s) = Cd*A*sqrt(2*g*H), Cd={cd}, D={outletDiameter} m, H above the orifice centre (bottomElevation + D/2).";
      }
      else
      {
        dischargeNote = isWeir
          ? "Discharge not computed: pass weirLength (m) with outletType 'weir'."
          : "Discharge not computed: pass outletDiameter (m) with outletType 'orifice'.";
      }

      var rows = new List<object?>();
      var baselineVolume = Measure(bottomElevation).Volume;
      var previousCumulative = 0.0;
      foreach (var h in levels)
      {
        var m = Measure(h);
        var cumulative = m.Volume - baselineVolume;
        var row = new Dictionary<string, object?>
        {
          ["elevation"] = Math.Round(h, 4),
          ["area"] = Math.Round(m.Area, 3),
          ["incrementalVolume"] = Math.Round(cumulative - previousCumulative, 3),
          ["cumulativeVolume"] = Math.Round(cumulative, 3),
        };
        if (dischargeModel != null) row["discharge"] = Math.Round(dischargeModel(h), 4);
        rows.Add(row);
        previousCumulative = cumulative;
      }

      return new Dictionary<string, object?>
      {
        ["surfaceName"] = surface.Name,
        ["bottomElevation"] = bottomElevation,
        ["topElevation"] = topElevation,
        ["elevationIncrement"] = elevIncrement,
        ["method"] = "Exact TIN clipping at each elevation (drawing units: m2 / m3 in a metric drawing)",
        ["connectivity"] = seedTri < 0 ? "all-triangles-below-elevation" : "connected-to-seedPoint",
        ["seedPoint"] = seedTri < 0 ? null : new Dictionary<string, object?> { ["x"] = seedX, ["y"] = seedY },
        ["triangleCount"] = tris.Count,
        ["totalStorage"] = Math.Round(previousCumulative, 3),
        ["table"] = rows,
        ["notes"] = new[]
        {
          seedTri < 0
            ? "Without seedPoint, every part of the surface below each elevation counts, including pockets not connected to the basin. Pass seedPoint {x, y} (a point at the basin floor) to count only the connected region."
            : "Only the region connected to seedPoint through flooded triangle edges is counted at each elevation.",
          dischargeNote,
        },
      };
    });
  }

  private sealed record StageTri(
    double X1, double Y1, double Z1, double X2, double Y2, double Z2, double X3, double Y3, double Z3,
    int V1, int V2, int V3)
  {
    public double MinZ => Math.Min(Z1, Math.Min(Z2, Z3));
    public double Plan => Math.Abs((X2 - X1) * (Y3 - Y1) - (X3 - X1) * (Y2 - Y1)) / 2.0;

    public IEnumerable<(int, int)> Edges()
    {
      yield return (Math.Min(V1, V2), Math.Max(V1, V2));
      yield return (Math.Min(V2, V3), Math.Max(V2, V3));
      yield return (Math.Min(V3, V1), Math.Max(V3, V1));
    }

    public double EdgeMinZ((int, int) e)
    {
      var min = double.MaxValue;
      if (V1 == e.Item1 || V1 == e.Item2) min = Math.Min(min, Z1);
      if (V2 == e.Item1 || V2 == e.Item2) min = Math.Min(min, Z2);
      if (V3 == e.Item1 || V3 == e.Item2) min = Math.Min(min, Z3);
      return min;
    }

    public bool ContainsXY(double x, double y)
    {
      static double Cross(double ax, double ay, double bx, double by, double px, double py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);
      var d1 = Cross(X1, Y1, X2, Y2, x, y);
      var d2 = Cross(X2, Y2, X3, Y3, x, y);
      var d3 = Cross(X3, Y3, X1, Y1, x, y);
      var neg = d1 < 0 || d2 < 0 || d3 < 0;
      var pos = d1 > 0 || d2 > 0 || d3 > 0;
      return !(neg && pos);
    }

    private (double A, double B, double C) Sorted()
    {
      var z = new[] { Z1, Z2, Z3 };
      Array.Sort(z);
      return (z[0], z[1], z[2]);
    }

    public double AreaBelow(double h)
    {
      var (a, b, c) = Sorted();
      var plan = Plan;
      if (c - a < 1e-12) return h > a ? plan : 0;
      if (h <= a) return 0;
      if (h >= c) return plan;
      if (h <= b) return plan * (h - a) * (h - a) / ((b - a) * (c - a));
      return plan - plan * (c - h) * (c - h) / ((c - b) * (c - a));
    }

    public double VolumeBelow(double h)
    {
      var (a, b, c) = Sorted();
      if (h <= a) return 0;
      double Piece(double lo, double hi) =>
        hi <= lo ? 0 : (hi - lo) / 6.0 * (AreaBelow(lo) + 4 * AreaBelow((lo + hi) / 2) + AreaBelow(hi));
      var v = Piece(a, Math.Min(h, b));
      if (h > b) v += Piece(b, Math.Min(h, c));
      if (h > c) v += Plan * (h - c);
      return v;
    }
  }

  // ─── Private helpers ─────────────────────────────────────────────────────────

  private static double EstimateBasinDepth(double targetVolumeCf, double bottomWidth, double sideSlope)
  {
    double basinLength = 2.0 * bottomWidth;
    double depth = 1.0;
    for (int iter = 0; iter < 50; iter++)
    {
      double vol = TrapezoidalVolume(depth, bottomWidth, basinLength, sideSlope);
      if (Math.Abs(vol - targetVolumeCf) < 1.0) break;
      depth *= (targetVolumeCf / vol);
      depth = Math.Max(0.5, Math.Min(depth, 50.0));
    }
    return Math.Max(1.0, depth);
  }

  private static double TrapezoidalVolume(double depth, double bottomWidth, double bottomLength, double sideSlope)
  {
    double topWidth = bottomWidth + 2 * sideSlope * depth;
    double topLength = bottomLength + 2 * sideSlope * depth;
    // Frustum formula: V = d/6 * (A_bottom + A_top + 4*A_mid)
    double aMid = (bottomWidth + sideSlope * depth) * (bottomLength + sideSlope * depth);
    return depth / 6.0 * (bottomWidth * bottomLength + topWidth * topLength + 4 * aMid);
  }

}
