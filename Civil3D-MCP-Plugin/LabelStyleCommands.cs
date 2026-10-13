using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace Civil3DMcpPlugin;

/// <summary>
/// Reads label-style components and creates new label styles by copying an existing one
/// (StyleBase.CopyAsSibling) and editing its text components (contents, height, offsets,
/// visibility). Verified in the AeccDbMgd 2026 metadata.
/// </summary>
public static class LabelStyleCommands
{
  public static Task<object?> GetLabelStyleAsync(JsonObject? parameters)
  {
    var kind = PluginRuntime.GetRequiredString(parameters, "kind");
    var styleName = PluginRuntime.GetRequiredString(parameters, "styleName");

    return CivilExecution.ReadAsync<object?>((doc, civilDocObject, database, transaction) =>
    {
      var civilDoc = (CivilDocument)civilDocObject;
      var (styleId, name) = LabelBatchCommands.ResolveStyle(civilDoc, transaction, kind, styleName);
      var style = CivilObjectUtils.GetRequiredObject<LabelStyle>(transaction, styleId, OpenMode.ForRead);
      return new Dictionary<string, object?>
      {
        ["kind"] = kind,
        ["name"] = name,
        ["components"] = DescribeComponents(style, transaction),
      };
    });
  }

  public static Task<object?> CreateLabelStyleAsync(JsonObject? parameters)
  {
    var kind = PluginRuntime.GetRequiredString(parameters, "kind");
    var baseStyle = PluginRuntime.GetRequiredString(parameters, "baseStyle");
    var newName = PluginRuntime.GetRequiredString(parameters, "newName");
    var components = (PluginRuntime.GetParameter(parameters, "components") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();

    return CivilExecution.WriteAsync<object?>((doc, civilDocObject, database, transaction) =>
    {
      var civilDoc = (CivilDocument)civilDocObject;
      var collection = LabelBatchCommands.GetTypedLabelStyles(civilDoc, kind)
        ?? throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", $"No label style collection for '{kind}'.");
      foreach (ObjectId existingId in collection)
      {
        var existing = CivilObjectUtils.GetRequiredObject<LabelStyle>(transaction, existingId, OpenMode.ForRead);
        if (string.Equals(existing.Name, newName, StringComparison.OrdinalIgnoreCase))
          throw new JsonRpcDispatchException("CIVIL3D.CONFLICT", $"Label style '{newName}' ({kind}) already exists.");
      }

      var (baseId, _) = LabelBatchCommands.ResolveStyle(civilDoc, transaction, kind, baseStyle);
      var source = CivilObjectUtils.GetRequiredObject<LabelStyle>(transaction, baseId, OpenMode.ForRead);
      var newId = source.CopyAsSibling(newName);
      var style = CivilObjectUtils.GetRequiredObject<LabelStyle>(transaction, newId, OpenMode.ForWrite);

      ApplyComponentEdits(style, components, transaction, baseStyle, "created");

      return new Dictionary<string, object?>
      {
        ["kind"] = kind,
        ["name"] = newName,
        ["baseStyle"] = baseStyle,
        ["handle"] = CivilObjectUtils.GetHandle(style),
        ["components"] = DescribeComponents(style, transaction),
      };
    });
  }

  public static Task<object?> EditLabelStyleAsync(JsonObject? parameters)
  {
    var kind = PluginRuntime.GetRequiredString(parameters, "kind");
    var styleName = PluginRuntime.GetRequiredString(parameters, "styleName");
    var components = (PluginRuntime.GetParameter(parameters, "components") as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    if (components.Count == 0)
      throw new JsonRpcDispatchException("CIVIL3D.INVALID_INPUT", "Provide at least one component to edit.");

    return CivilExecution.WriteAsync<object?>((doc, civilDocObject, database, transaction) =>
    {
      var civilDoc = (CivilDocument)civilDocObject;
      var (styleId, name) = LabelBatchCommands.ResolveStyle(civilDoc, transaction, kind, styleName);
      var style = CivilObjectUtils.GetRequiredObject<LabelStyle>(transaction, styleId, OpenMode.ForWrite);
      ApplyComponentEdits(style, components, transaction, name, "changed");
      return new Dictionary<string, object?>
      {
        ["kind"] = kind,
        ["name"] = name,
        ["components"] = DescribeComponents(style, transaction),
      };
    });
  }

  // Shared by create (on the fresh copy) and edit (in place): components are addressed by name and an
  // unknown name aborts the whole transaction.
  private static void ApplyComponentEdits(LabelStyle style, List<JsonObject> components, Transaction transaction, string styleName, string verb)
  {
      var byName = AllComponents(style, transaction).ToDictionary(item => item.Name, item => item, StringComparer.OrdinalIgnoreCase);
      for (var index = 0; index < components.Count; index++)
      {
        var spec = components[index];
        var componentName = PluginRuntime.GetRequiredString(spec, "name");
        if (!byName.TryGetValue(componentName, out var entry))
        {
          throw new JsonRpcDispatchException(
            "CIVIL3D.OBJECT_NOT_FOUND",
            $"components[{index}]: '{componentName}' is not a component of '{styleName}'. Components: {string.Join(", ", byName.Keys)}. Nothing was {verb}.");
        }

        var component = CivilObjectUtils.GetRequiredObject<LabelStyleComponent>(transaction, entry.Id, OpenMode.ForWrite);
        try
        {
          var visible = PluginRuntime.GetOptionalBool(spec, "visible");
          if (visible.HasValue) ((dynamic)component).General.Visible.Value = visible.Value;

          if (component is LabelStyleTextComponent text)
          {
            var contents = PluginRuntime.GetOptionalString(spec, "contents");
            if (contents != null) text.Text.Contents.Value = contents;
            var height = PluginRuntime.GetOptionalDouble(spec, "height");
            if (height.HasValue) text.Text.Height.Value = height.Value;
            var xOffset = PluginRuntime.GetOptionalDouble(spec, "xOffset");
            if (xOffset.HasValue) text.Text.XOffset.Value = xOffset.Value;
            var yOffset = PluginRuntime.GetOptionalDouble(spec, "yOffset");
            if (yOffset.HasValue) text.Text.YOffset.Value = yOffset.Value;
            var angle = PluginRuntime.GetOptionalDouble(spec, "angle");
            if (angle.HasValue) text.Text.Angle.Value = angle.Value * Math.PI / 180.0;
            var maxWidth = PluginRuntime.GetOptionalDouble(spec, "maxWidth");
            if (maxWidth.HasValue) text.Text.MaxWidth.Value = maxWidth.Value;
          }
          else if (spec.ContainsKey("contents") || spec.ContainsKey("height"))
          {
            throw new ArgumentException($"'{componentName}' is a {entry.Type} component; only text components take contents/height.");
          }
        }
        catch (JsonRpcDispatchException)
        {
          throw;
        }
        catch (Exception exception)
        {
          throw new JsonRpcDispatchException("CIVIL3D.API_ERROR", $"components[{index}] '{componentName}' failed: {exception.Message} Nothing was {verb}.");
        }
      }

  }

  private static IEnumerable<(string Name, ObjectId Id, string Type)> AllComponents(LabelStyle style, Transaction transaction)
  {
    foreach (LabelStyleComponentType type in Enum.GetValues(typeof(LabelStyleComponentType)))
    {
      ObjectIdCollection ids;
      try
      {
        if (!style.IsSupportedComponent(type)) continue;
        ids = style.GetComponents(type);
      }
      catch
      {
        continue;
      }
      foreach (ObjectId id in ids)
      {
        var component = CivilObjectUtils.GetRequiredObject<LabelStyleComponent>(transaction, id, OpenMode.ForRead);
        yield return (CivilObjectUtils.GetName(component) ?? id.Handle.ToString(), id, type.ToString());
      }
    }
  }

  private static List<Dictionary<string, object?>> DescribeComponents(LabelStyle style, Transaction transaction)
  {
    var result = new List<Dictionary<string, object?>>();
    foreach (var (name, id, type) in AllComponents(style, transaction))
    {
      var component = CivilObjectUtils.GetRequiredObject<LabelStyleComponent>(transaction, id, OpenMode.ForRead);
      var entry = new Dictionary<string, object?> { ["name"] = name, ["type"] = type };
      try
      {
        entry["visible"] = (bool)((dynamic)component).General.Visible.Value;
      }
      catch
      {
        entry["visible"] = null;
      }
      if (component is LabelStyleTextComponent text)
      {
        entry["contents"] = text.Text.Contents.Value;
        entry["height"] = text.Text.Height.Value;
        entry["xOffset"] = text.Text.XOffset.Value;
        entry["yOffset"] = text.Text.YOffset.Value;
        entry["angle"] = Math.Round(text.Text.Angle.Value * 180.0 / Math.PI, 4);
        entry["anchorComponent"] = text.General.AnchorComponent.Value;
        entry["anchorPoint"] = text.General.AnchorLocation.Value.ToString();
      }
      else if (component is LabelStyleTextForEachComponent forEach)
      {
        entry["contents"] = forEach.Text.Contents.Value;
        entry["height"] = forEach.Text.Height.Value;
      }
      else if (component is LabelStyleReferenceTextComponent reference)
      {
        entry["contents"] = reference.Text.Contents.Value;
        entry["height"] = reference.Text.Height.Value;
      }
      result.Add(entry);
    }
    return result;
  }
}
