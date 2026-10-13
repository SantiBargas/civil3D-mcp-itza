using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Civil3DMcpPlugin;

/// <summary>
/// Paper-space sheets without Plan Production: layouts with page setup and scaled viewports,
/// batch drafting in a layout (or model space), layer listing and multi-sheet PDF output through
/// PlotEngine. Members verified in the AcDbMgd/accoremgd 2026 metadata.
/// </summary>
public static class LayoutCommands
{
  private const string DefaultDevice = "DWG To PDF.pc3";

  public static Task<object?> CreateLayoutAsync(JsonObject? parameters)
  {
    var layoutName = PluginRuntime.GetRequiredString(parameters, "layoutName");
    var device = PluginRuntime.GetOptionalString(parameters, "device") ?? DefaultDevice;
    var paperWidth = PluginRuntime.GetOptionalDouble(parameters, "paperWidth") ?? 420.0;
    var paperHeight = PluginRuntime.GetOptionalDouble(parameters, "paperHeight") ?? 297.0;
    var media = PluginRuntime.GetOptionalString(parameters, "media");
    var viewports = (PluginRuntime.GetParameter(parameters, "viewports") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var layoutManager = LayoutManager.Current;
      if (!layoutManager.GetLayoutId(layoutName).IsNull)
      {
        throw new JsonRpcDispatchException("CIVIL3D.CONFLICT", $"Layout '{layoutName}' already exists.");
      }

      var layoutId = layoutManager.CreateLayout(layoutName);
      var layout = CivilObjectUtils.GetRequiredObject<Layout>(transaction, layoutId, OpenMode.ForWrite);
      var validator = PlotSettingsValidator.Current;
      ApplyPageSetup(validator, layout, device, paperWidth, paperHeight, media);

      // Activating the layout creates its paper-space viewport; the default model viewport it may
      // add (LAYOUTCREATEVIEWPORT) is removed so only the requested ones remain.
      layoutManager.CurrentLayout = layoutName;
      var existing = layout.GetViewports();
      for (var i = 1; i < existing.Count; i++)
      {
        CivilObjectUtils.GetRequiredObject<Viewport>(transaction, existing[i], OpenMode.ForWrite).Erase();
      }

      var mmPerUnit = MillimetersPerDrawingUnit(database);
      var layoutBlock = CivilObjectUtils.GetRequiredObject<BlockTableRecord>(transaction, layout.BlockTableRecordId, OpenMode.ForWrite);
      var layerTable = CivilObjectUtils.GetRequiredObject<LayerTable>(transaction, database.LayerTableId, OpenMode.ForRead);
      var created = new List<Dictionary<string, object?>>();

      for (var index = 0; index < viewports.Count; index++)
      {
        var spec = viewports[index];
        var denominator = PluginRuntime.GetRequiredDouble(spec, "scale");
        if (denominator <= 0)
          throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"viewports[{index}].scale must be positive (e.g. 1000 for 1:1000).");

        var viewport = new Viewport
        {
          CenterPoint = new Point3d(PluginRuntime.GetRequiredDouble(spec, "x"), PluginRuntime.GetRequiredDouble(spec, "y"), 0.0),
          Width = PluginRuntime.GetRequiredDouble(spec, "width"),
          Height = PluginRuntime.GetRequiredDouble(spec, "height"),
        };
        layoutBlock.AppendEntity(viewport);
        transaction.AddNewlyCreatedDBObject(viewport, true);
        viewport.ViewDirection = Vector3d.ZAxis;
        viewport.ViewTarget = Point3d.Origin;
        viewport.ViewCenter = new Point2d(PluginRuntime.GetRequiredDouble(spec, "viewCenterX"), PluginRuntime.GetRequiredDouble(spec, "viewCenterY"));
        // CustomScale = paper units per model unit; paper is in mm.
        viewport.CustomScale = mmPerUnit / denominator;
        var annotationScale = SetAnnotationScale(database, viewport, denominator, PluginRuntime.GetOptionalString(spec, "annotationScale"));
        viewport.On = true;

        var frozen = new List<string>();
        var missing = new List<string>();
        var layerIds = new ObjectIdCollection();
        foreach (var node in spec["frozenLayers"] as JsonArray ?? new JsonArray())
        {
          var name = node?.GetValue<string>();
          if (string.IsNullOrWhiteSpace(name)) continue;
          if (layerTable.Has(name))
          {
            layerIds.Add(layerTable[name]);
            frozen.Add(name);
          }
          else
          {
            missing.Add(name);
          }
        }
        if (layerIds.Count > 0) viewport.FreezeLayersInViewport(layerIds.GetEnumerator());
        viewport.Locked = PluginRuntime.GetOptionalBool(spec, "locked") ?? true;

        created.Add(new Dictionary<string, object?>
        {
          ["handle"] = CivilObjectUtils.GetHandle(viewport),
          ["scale"] = $"1:{denominator}",
          ["customScale"] = viewport.CustomScale,
          ["annotationScale"] = annotationScale,
          ["frozenLayers"] = frozen,
          ["missingLayers"] = missing,
        });
      }

      layoutManager.CurrentLayout = "Model";

      return new Dictionary<string, object?>
      {
        ["layoutName"] = layoutName,
        ["device"] = layout.PlotConfigurationName,
        ["media"] = layout.CanonicalMediaName,
        ["paperSize"] = new[] { Math.Round(layout.PlotPaperSize.X, 2), Math.Round(layout.PlotPaperSize.Y, 2) },
        ["viewports"] = created,
      };
    });
  }

  // Freezes (or thaws) layers in the model viewports of an existing layout.
  public static Task<object?> FreezeViewportLayersAsync(JsonObject? parameters)
  {
    var layoutName = PluginRuntime.GetRequiredString(parameters, "layoutName");
    var thaw = PluginRuntime.GetOptionalBool(parameters, "thaw") ?? false;
    var names = (PluginRuntime.GetParameter(parameters, "layers") as JsonArray)?
      .Select(node => node?.GetValue<string>())
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .Select(name => name!)
      .ToList() ?? new List<string>();
    if (names.Count == 0)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Provide at least one layer.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var layoutId = LayoutManager.Current.GetLayoutId(layoutName);
      if (layoutId.IsNull) throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Layout '{layoutName}' was not found.");
      var layerTable = CivilObjectUtils.GetRequiredObject<LayerTable>(transaction, database.LayerTableId, OpenMode.ForRead);
      var missing = names.Where(name => !layerTable.Has(name)).ToList();
      if (missing.Count > 0)
        throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Layers not found: {string.Join(", ", missing)}. Nothing was changed.");
      var ids = new ObjectIdCollection();
      foreach (var name in names) ids.Add(layerTable[name]);

      var layout = CivilObjectUtils.GetRequiredObject<Layout>(transaction, layoutId, OpenMode.ForRead);
      var viewportIds = layout.GetViewports();
      var count = 0;
      for (var i = 1; i < viewportIds.Count; i++)
      {
        var viewport = CivilObjectUtils.GetRequiredObject<Viewport>(transaction, viewportIds[i], OpenMode.ForWrite);
        if (thaw) viewport.ThawLayersInViewport(ids.GetEnumerator());
        else viewport.FreezeLayersInViewport(ids.GetEnumerator());
        count++;
      }

      return new Dictionary<string, object?>
      {
        ["layoutName"] = layoutName,
        ["viewports"] = count,
        [thaw ? "thawed" : "frozen"] = names,
      };
    });
  }

  public static Task<object?> DeleteLayoutAsync(JsonObject? parameters)
  {
    var layoutName = PluginRuntime.GetRequiredString(parameters, "layoutName");
    if (string.Equals(layoutName, "Model", StringComparison.OrdinalIgnoreCase))
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Model space cannot be deleted.");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var layoutManager = LayoutManager.Current;
      if (layoutManager.GetLayoutId(layoutName).IsNull)
        throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Layout '{layoutName}' was not found.");
      if (string.Equals(layoutManager.CurrentLayout, layoutName, StringComparison.OrdinalIgnoreCase))
        layoutManager.CurrentLayout = "Model";
      layoutManager.DeleteLayout(layoutName);
      return new Dictionary<string, object?> { ["layoutName"] = layoutName, ["deleted"] = true };
    });
  }

  // Rescales existing viewports of a layout (all of them, or one by handle) and sets the matching
  // annotation scale.
  public static Task<object?> SetViewportScaleAsync(JsonObject? parameters)
  {
    var layoutName = PluginRuntime.GetRequiredString(parameters, "layoutName");
    var denominator = PluginRuntime.GetRequiredDouble(parameters, "scale");
    var viewportHandle = PluginRuntime.GetOptionalString(parameters, "viewportHandle");
    var requestedAnnotationScale = PluginRuntime.GetOptionalString(parameters, "annotationScale");
    if (denominator <= 0)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "scale must be positive (e.g. 1000 for 1:1000).");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var layoutId = LayoutManager.Current.GetLayoutId(layoutName);
      if (layoutId.IsNull) throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Layout '{layoutName}' was not found.");
      var layout = CivilObjectUtils.GetRequiredObject<Layout>(transaction, layoutId, OpenMode.ForRead);
      var viewportIds = layout.GetViewports();
      var mmPerUnit = MillimetersPerDrawingUnit(database);
      var updated = new List<Dictionary<string, object?>>();

      // Index 0 is the paper-space viewport itself.
      for (var i = 1; i < viewportIds.Count; i++)
      {
        var viewport = CivilObjectUtils.GetRequiredObject<Viewport>(transaction, viewportIds[i], OpenMode.ForWrite);
        var handle = CivilObjectUtils.GetHandle(viewport);
        if (viewportHandle != null && !string.Equals(handle, viewportHandle, StringComparison.OrdinalIgnoreCase)) continue;
        var locked = viewport.Locked;
        viewport.Locked = false;
        viewport.CustomScale = mmPerUnit / denominator;
        var annotationScale = SetAnnotationScale(database, viewport, denominator, requestedAnnotationScale);
        viewport.Locked = locked;
        updated.Add(new Dictionary<string, object?>
        {
          ["handle"] = handle,
          ["scale"] = $"1:{denominator}",
          ["customScale"] = viewport.CustomScale,
          ["annotationScale"] = annotationScale,
        });
      }

      if (updated.Count == 0)
        throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", viewportHandle == null ? $"Layout '{layoutName}' has no model viewports." : $"Viewport '{viewportHandle}' was not found in '{layoutName}'.");

      return new Dictionary<string, object?>
      {
        ["layoutName"] = layoutName,
        ["viewports"] = updated,
      };
    });
  }

  // Civil 3D labels size themselves with the viewport annotation scale; a new viewport starts at
  // 1:1, which left every label in a 1:1000 sheet invisible (seen live). Uses the drawing's scale
  // named "1:N" (or the requested name), else one with ratio 1/N, else creates "1:N".
  private static string SetAnnotationScale(Database database, Viewport viewport, double denominator, string? requestedName)
  {
    var scales = database.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
    var name = string.IsNullOrWhiteSpace(requestedName) ? FormattableString.Invariant($"1:{denominator:0.###}") : requestedName!;
    AnnotationScale? chosen = null;
    if (scales.HasContext(name))
    {
      chosen = scales.GetContext(name) as AnnotationScale;
    }
    else if (!string.IsNullOrWhiteSpace(requestedName))
    {
      throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Annotation scale '{requestedName}' does not exist in the drawing.");
    }

    // Civil 3D label heights are paper sizes in drawing units, so the annotation ratio is the plain
    // 1/N (unlike the viewport CustomScale, which converts mm of paper to drawing units).
    if (chosen == null)
    {
      foreach (ObjectContext context in scales)
      {
        if (context is AnnotationScale scale && Math.Abs(scale.Scale - 1.0 / denominator) < 1e-12)
        {
          chosen = scale;
          break;
        }
      }
    }

    // Creating a scale here (AddContext) crashed Civil 3D outright for 1:1250 (seen live, no managed
    // exception), so a missing scale is reported instead of created.
    if (chosen == null)
    {
      var available = new List<string>();
      foreach (ObjectContext context in scales)
      {
        if (context is AnnotationScale scale) available.Add(scale.Name);
      }
      throw new JsonRpcDispatchException(
        "CIVIL3D.INVALID_INPUT",
        $"The drawing has no annotation scale for 1:{denominator}. Use one of: {string.Join(", ", available)}. Nothing was changed.");
    }

    viewport.AnnotationScale = chosen;
    return chosen.Name;
  }

  // One transaction for every entity; layoutName "Model" draws in model space.
  public static Task<object?> AddLayoutEntitiesAsync(JsonObject? parameters)
  {
    var layoutName = PluginRuntime.GetRequiredString(parameters, "layoutName");
    var layerName = PluginRuntime.GetOptionalString(parameters, "layer");
    var lines = Objects(parameters, "lines");
    var polylines = Objects(parameters, "polylines");
    var texts = Objects(parameters, "texts");
    var tables = Objects(parameters, "tables");
    if (lines.Count + polylines.Count + texts.Count + tables.Count == 0)
    {
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Provide at least one line, polyline, text or table.");
    }

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var space = GetSpace(database, transaction, layoutName);
      var layerId = LookupUtils.GetLayerId(database, transaction, layerName);
      var count = 0;

      void Add(Autodesk.AutoCAD.DatabaseServices.Entity entity)
      {
        entity.LayerId = layerId;
        space.AppendEntity(entity);
        transaction.AddNewlyCreatedDBObject(entity, true);
        if (entity is DBText text && (text.HorizontalMode != TextHorizontalMode.TextLeft || text.VerticalMode != TextVerticalMode.TextBase))
        {
          text.AdjustAlignment(database);
        }
        count++;
      }

      for (var i = 0; i < lines.Count; i++)
      {
        var line = lines[i];
        Guard("lines", i, () => Add(new Line(
          new Point3d(PluginRuntime.GetRequiredDouble(line, "x1"), PluginRuntime.GetRequiredDouble(line, "y1"), 0.0),
          new Point3d(PluginRuntime.GetRequiredDouble(line, "x2"), PluginRuntime.GetRequiredDouble(line, "y2"), 0.0))));
      }

      for (var i = 0; i < polylines.Count; i++)
      {
        var spec = polylines[i];
        Guard("polylines", i, () =>
        {
          var points = (spec["points"] as JsonArray ?? new JsonArray()).ToList();
          if (points.Count < 2) throw new ArgumentException("needs at least 2 points");
          var polyline = new Polyline();
          for (var p = 0; p < points.Count; p++)
          {
            var point = ReadPoint(points[p]);
            polyline.AddVertexAt(p, point, 0, 0, 0);
          }
          polyline.Closed = PluginRuntime.GetOptionalBool(spec, "closed") ?? false;
          var width = PluginRuntime.GetOptionalDouble(spec, "width") ?? 0.0;
          if (width > 0) polyline.ConstantWidth = width;
          Add(polyline);
        });
      }

      for (var i = 0; i < texts.Count; i++)
      {
        var spec = texts[i];
        Guard("texts", i, () => Add(MakeText(
          PluginRuntime.GetRequiredString(spec, "text"),
          PluginRuntime.GetRequiredDouble(spec, "x"),
          PluginRuntime.GetRequiredDouble(spec, "y"),
          PluginRuntime.GetRequiredDouble(spec, "height"),
          PluginRuntime.GetOptionalString(spec, "align") ?? "left",
          PluginRuntime.GetOptionalDouble(spec, "rotation") ?? 0.0)));
      }

      // Tables are drawn as plain lines and texts: predictable output without a table style.
      for (var i = 0; i < tables.Count; i++)
      {
        var spec = tables[i];
        Guard("tables", i, () =>
        {
          var left = PluginRuntime.GetRequiredDouble(spec, "x");
          var top = PluginRuntime.GetRequiredDouble(spec, "y");
          var rowHeight = PluginRuntime.GetRequiredDouble(spec, "rowHeight");
          var textHeight = PluginRuntime.GetOptionalDouble(spec, "textHeight") ?? rowHeight * 0.4;
          var widths = (spec["columnWidths"] as JsonArray ?? new JsonArray()).Select(node => node!.GetValue<double>()).ToList();
          var rows = (spec["rows"] as JsonArray ?? new JsonArray()).Select(row => (row as JsonArray ?? new JsonArray()).Select(cell => cell?.ToString() ?? string.Empty).ToList()).ToList();
          if (widths.Count == 0 || rows.Count == 0) throw new ArgumentException("needs columnWidths and rows");
          var aligns = (spec["columnAlign"] as JsonArray ?? new JsonArray()).Select(node => node?.GetValue<string>() ?? "left").ToList();
          var totalWidth = widths.Sum();
          var bottom = top - rowHeight * rows.Count;

          for (var r = 0; r <= rows.Count; r++)
          {
            var y = top - rowHeight * r;
            Add(new Line(new Point3d(left, y, 0), new Point3d(left + totalWidth, y, 0)));
          }
          var x = left;
          for (var c = 0; c <= widths.Count; c++)
          {
            Add(new Line(new Point3d(x, top, 0), new Point3d(x, bottom, 0)));
            if (c < widths.Count) x += widths[c];
          }

          for (var r = 0; r < rows.Count; r++)
          {
            var cellLeft = left;
            var middle = top - rowHeight * r - rowHeight / 2.0;
            for (var c = 0; c < widths.Count && c < rows[r].Count; c++)
            {
              var value = rows[r][c];
              var align = c < aligns.Count ? aligns[c] : "left";
              if (!string.IsNullOrEmpty(value))
              {
                var pad = textHeight * 0.6;
                var tx = align switch
                {
                  "center" => cellLeft + widths[c] / 2.0,
                  "right" => cellLeft + widths[c] - pad,
                  _ => cellLeft + pad,
                };
                Add(MakeText(value, tx, middle, textHeight, align switch { "center" => "middle-center", "right" => "middle-right", _ => "middle-left" }, 0.0));
              }
              cellLeft += widths[c];
            }
          }
        });
      }

      return new Dictionary<string, object?>
      {
        ["layoutName"] = layoutName,
        ["layer"] = string.IsNullOrWhiteSpace(layerName) ? null : layerName,
        ["entitiesCreated"] = count,
      };
    });
  }

  public static Task<object?> ListLayersAsync(JsonObject? parameters)
  {
    return CivilExecution.ReadAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      var layerTable = CivilObjectUtils.GetRequiredObject<LayerTable>(transaction, database.LayerTableId, OpenMode.ForRead);
      var layers = new List<Dictionary<string, object?>>();
      foreach (ObjectId layerId in layerTable)
      {
        var layer = CivilObjectUtils.GetRequiredObject<LayerTableRecord>(transaction, layerId, OpenMode.ForRead);
        layers.Add(new Dictionary<string, object?>
        {
          ["name"] = layer.Name,
          ["isOff"] = layer.IsOff,
          ["isFrozen"] = layer.IsFrozen,
          ["color"] = layer.Color.ColorIndex,
        });
      }

      var layouts = new List<string>();
      var dictionary = CivilObjectUtils.GetRequiredObject<DBDictionary>(transaction, database.LayoutDictionaryId, OpenMode.ForRead);
      foreach (DBDictionaryEntry entry in dictionary) layouts.Add(entry.Key);

      return new Dictionary<string, object?>
      {
        ["layers"] = layers.OrderBy(layer => (string)layer["name"]!, StringComparer.OrdinalIgnoreCase).ToList(),
        ["layouts"] = layouts,
      };
    });
  }

  public static Task<object?> PublishPdfAsync(JsonObject? parameters)
  {
    var layoutNames = (PluginRuntime.GetParameter(parameters, "layoutNames") as JsonArray)?
      .Select(node => node?.GetValue<string>())
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .Select(name => name!)
      .ToList() ?? new List<string>();
    if (layoutNames.Count == 0)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Parameter 'layoutNames' must be a non-empty array.");
    var overwrite = PluginRuntime.GetOptionalBool(parameters, "overwrite") ?? false;
    var outputPath = FileBoundary.ResolveExportPath(PluginRuntime.GetRequiredString(parameters, "outputPath"), overwrite, ".pdf");

    return CivilExecution.WriteAsync<object?>((doc, civilDoc, database, transaction) =>
    {
      if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
        throw new JsonRpcDispatchException("CIVIL3D.CONFLICT", "AutoCAD is already plotting; try again when it finishes.");

      var layoutManager = LayoutManager.Current;
      var layoutIds = layoutNames.Select(name =>
      {
        var id = layoutManager.GetLayoutId(name);
        if (id.IsNull) throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Layout '{name}' was not found.");
        return id;
      }).ToList();

      if (File.Exists(outputPath)) File.Delete(outputPath);
      var previousBackgroundPlot = AcadApplication.GetSystemVariable("BACKGROUNDPLOT");
      var previousLayout = layoutManager.CurrentLayout;
      // Foreground plotting so the PDF exists when PlotEngine returns.
      AcadApplication.SetSystemVariable("BACKGROUNDPLOT", (short)0);
      try
      {
        using var engine = PlotFactory.CreatePublishEngine();
        using var progress = new PlotProgressDialog(false, layoutIds.Count, false);
        progress.OnBeginPlot();
        progress.IsVisible = false;
        engine.BeginPlot(progress, null);

        for (var i = 0; i < layoutIds.Count; i++)
        {
          layoutManager.CurrentLayout = layoutNames[i];
          var layout = CivilObjectUtils.GetRequiredObject<Layout>(transaction, layoutIds[i], OpenMode.ForRead);
          var settings = new PlotSettings(layout.ModelType);
          settings.CopyFrom(layout);
          if (string.IsNullOrWhiteSpace(settings.PlotConfigurationName) || string.Equals(settings.PlotConfigurationName, "None", StringComparison.OrdinalIgnoreCase))
          {
            // Layouts without a plot device (e.g. the template's Layout1) get the PDF device on A3.
            ApplyPageSetup(PlotSettingsValidator.Current, settings, DefaultDevice, 420.0, 297.0, null);
          }
          var info = new PlotInfo { Layout = layoutIds[i], OverrideSettings = settings };
          var infoValidator = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
          infoValidator.Validate(info);

          if (i == 0)
          {
            engine.BeginDocument(info, doc.Name, null, 1, true, outputPath);
          }
          progress.OnBeginSheet();
          engine.BeginPage(new PlotPageInfo(), info, i == layoutIds.Count - 1, null);
          engine.BeginGenerateGraphics(null);
          engine.EndGenerateGraphics(null);
          engine.EndPage(null);
          progress.OnEndSheet();
        }

        engine.EndDocument(null);
        progress.OnEndPlot();
        engine.EndPlot(null);
      }
      finally
      {
        AcadApplication.SetSystemVariable("BACKGROUNDPLOT", previousBackgroundPlot);
        layoutManager.CurrentLayout = previousLayout;
      }

      var file = new FileInfo(outputPath);
      if (!file.Exists || file.Length == 0)
        throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"PlotEngine finished but '{outputPath}' was not written.");

      return new Dictionary<string, object?>
      {
        ["outputPath"] = outputPath,
        ["sheetsPublished"] = layoutIds.Count,
        ["published"] = true,
        ["bytes"] = file.Length,
      };
    });
  }

  // SetClosestMediaName rejects A3 on "DWG To PDF.pc3" (eInvalidInput, seen live), so the media is
  // picked by its canonical name, which carries the size: "ISO_full_bleed_A3_(420.00_x_297.00_MM)".
  private static void ApplyPageSetup(PlotSettingsValidator validator, PlotSettings settings, string device, double width, double height, string? requestedMedia)
  {
    validator.SetPlotConfigurationName(settings, device, null);
    validator.RefreshLists(settings);
    var names = validator.GetCanonicalMediaNameList(settings).Cast<string>().ToList();
    var rotate = false;
    string media;
    if (!string.IsNullOrWhiteSpace(requestedMedia))
    {
      media = names.FirstOrDefault(name => string.Equals(name, requestedMedia, StringComparison.OrdinalIgnoreCase))
        ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Media '{requestedMedia}' is not available on '{device}'.");
    }
    else
    {
      static string Key(double a, double b) => FormattableString.Invariant($"{a:0.00}_x_{b:0.00}_MM");
      var matches = names.Where(name => name.Contains(Key(width, height), StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count == 0)
      {
        matches = names.Where(name => name.Contains(Key(height, width), StringComparison.OrdinalIgnoreCase)).ToList();
        rotate = matches.Count > 0;
      }
      if (matches.Count == 0)
      {
        throw new JsonRpcDispatchException(
          "CIVIL3D.INVALID_INPUT",
          $"No {width} x {height} mm media on '{device}'. Some available: {string.Join(", ", names.Take(20))}.");
      }
      media = matches.FirstOrDefault(name => name.Contains("full_bleed", StringComparison.OrdinalIgnoreCase)) ?? matches[0];
    }

    validator.SetCanonicalMediaName(settings, media);
    validator.SetPlotPaperUnits(settings, PlotPaperUnit.Millimeters);
    validator.SetPlotType(settings, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
    validator.SetUseStandardScale(settings, true);
    validator.SetStdScaleType(settings, StdScaleType.StdScale1To1);
    validator.SetPlotRotation(settings, rotate ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
  }

  private static List<JsonObject> Objects(JsonObject? parameters, string name)
    => (PluginRuntime.GetParameter(parameters, name) as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();

  private static void Guard(string list, int index, Action action)
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
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"{list}[{index}] failed: {exception.Message} Nothing was drawn.");
    }
  }

  private static Point2d ReadPoint(JsonNode? node)
  {
    if (node is JsonArray pair && pair.Count >= 2) return new Point2d(pair[0]!.GetValue<double>(), pair[1]!.GetValue<double>());
    if (node is JsonObject point) return new Point2d(PluginRuntime.GetRequiredDouble(point, "x"), PluginRuntime.GetRequiredDouble(point, "y"));
    throw new ArgumentException("points must be [x, y] or {x, y}");
  }

  private static DBText MakeText(string value, double x, double y, double height, string align, double rotationDegrees)
  {
    var position = new Point3d(x, y, 0.0);
    var text = new DBText
    {
      TextString = value,
      Height = height,
      Position = position,
      Rotation = rotationDegrees * Math.PI / 180.0,
    };
    var (horizontal, vertical) = align.ToLowerInvariant() switch
    {
      "left" => (TextHorizontalMode.TextLeft, TextVerticalMode.TextBase),
      "center" => (TextHorizontalMode.TextCenter, TextVerticalMode.TextBase),
      "right" => (TextHorizontalMode.TextRight, TextVerticalMode.TextBase),
      "middle-left" => (TextHorizontalMode.TextLeft, TextVerticalMode.TextVerticalMid),
      "middle-center" => (TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid),
      "middle-right" => (TextHorizontalMode.TextRight, TextVerticalMode.TextVerticalMid),
      _ => throw new ArgumentException($"align '{align}' is not one of left, center, right, middle-left, middle-center, middle-right"),
    };
    text.HorizontalMode = horizontal;
    text.VerticalMode = vertical;
    if (horizontal != TextHorizontalMode.TextLeft || vertical != TextVerticalMode.TextBase)
    {
      text.AlignmentPoint = position;
    }
    return text;
  }

  private static BlockTableRecord GetSpace(Database database, Transaction transaction, string layoutName)
  {
    if (string.Equals(layoutName, "Model", StringComparison.OrdinalIgnoreCase))
    {
      var blockTable = CivilObjectUtils.GetRequiredObject<BlockTable>(transaction, database.BlockTableId, OpenMode.ForRead);
      return CivilObjectUtils.GetRequiredObject<BlockTableRecord>(transaction, blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
    }

    var layoutId = LayoutManager.Current.GetLayoutId(layoutName);
    if (layoutId.IsNull) throw new JsonRpcDispatchException("CIVIL3D.OBJECT_NOT_FOUND", $"Layout '{layoutName}' was not found.");
    var layout = CivilObjectUtils.GetRequiredObject<Layout>(transaction, layoutId, OpenMode.ForRead);
    return CivilObjectUtils.GetRequiredObject<BlockTableRecord>(transaction, layout.BlockTableRecordId, OpenMode.ForWrite);
  }

  private static double MillimetersPerDrawingUnit(Database database) => database.Insunits switch
  {
    UnitsValue.Meters => 1000.0,
    UnitsValue.Centimeters => 10.0,
    UnitsValue.Millimeters => 1.0,
    UnitsValue.Kilometers => 1_000_000.0,
    UnitsValue.Feet => 304.8,
    UnitsValue.Inches => 25.4,
    _ => throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"Drawing units ({database.Insunits}) cannot be converted to a paper scale."),
  };
}
