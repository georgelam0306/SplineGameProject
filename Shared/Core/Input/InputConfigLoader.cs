using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Input;

/// <summary>
/// Loads input configuration from JSON.
/// Call during initialization, not per-frame.
/// </summary>
public static class InputConfigLoader
{
    /// <summary>
    /// Load action maps from a JSON file.
    /// </summary>
    public static void LoadFromFile(IInputManager manager, string filePath)
    {
        string json = File.ReadAllText(filePath);
        LoadFromJson(manager, json);
    }

    /// <summary>
    /// Load action maps from a JSON string.
    /// </summary>
    public static void LoadFromJson(IInputManager manager, string json)
    {
        var config = JsonSerializer.Deserialize(json, InputConfigJsonContext.Default.InputConfig);

        if (config?.ActionMaps == null)
        {
            return;
        }

        foreach (var mapConfig in config.ActionMaps)
        {
            if (string.IsNullOrEmpty(mapConfig.Name))
            {
                continue;
            }

            var map = manager.CreateActionMap(mapConfig.Name);
            if (map == null)
            {
                continue;
            }

            if (mapConfig.Actions == null)
            {
                continue;
            }

            foreach (var actionConfig in mapConfig.Actions)
            {
                if (string.IsNullOrEmpty(actionConfig.Name))
                {
                    continue;
                }

                var action = map.AddAction(actionConfig.Name, ParseActionType(actionConfig.Type));
                if (action == null)
                {
                    continue;
                }

                if (actionConfig.DeadZone.HasValue)
                {
                    action.DeadZone = actionConfig.DeadZone.Value;
                }

                if (actionConfig.DerivedTriggers != null)
                {
                    foreach (var trigger in actionConfig.DerivedTriggers)
                    {
                        if (trigger == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(trigger.SourceAction) || !trigger.Threshold.HasValue)
                        {
                            continue;
                        }

                        var kind = ParseDerivedTriggerKind(trigger.Kind);
                        var mode = ParseDerivedTriggerMode(trigger.Mode);
                        action.AddDerivedTrigger(trigger.SourceAction, kind, trigger.Threshold.Value, mode);
                    }
                }

                // Add direct bindings
                if (actionConfig.Bindings != null)
                {
                    foreach (var bindingPath in actionConfig.Bindings)
                    {
                        if (!string.IsNullOrEmpty(bindingPath))
                        {
                            action.AddBinding(bindingPath);
                        }
                    }
                }

                // Add composite bindings
                if (actionConfig.Composites != null)
                {
                    foreach (var composite in actionConfig.Composites)
                    {
                        if (!string.IsNullOrEmpty(composite.Name) &&
                            !string.IsNullOrEmpty(composite.Up) &&
                            !string.IsNullOrEmpty(composite.Down) &&
                            !string.IsNullOrEmpty(composite.Left) &&
                            !string.IsNullOrEmpty(composite.Right))
                        {
                            action.AddCompositeBinding(
                                composite.Name,
                                composite.Up,
                                composite.Down,
                                composite.Left,
                                composite.Right
                            );
                        }
                    }
                }
            }

            map.ResolveDerivedTriggers();
        }
    }

    private static ActionType ParseActionType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "button" => ActionType.Button,
            "value" => ActionType.Value,
            "vector2" => ActionType.Vector2,
            "vector3" => ActionType.Vector3,
            _ => ActionType.Button
        };
    }

    private static DerivedTriggerKind ParseDerivedTriggerKind(string? kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "vector2magnitudeatleast" or "vector2_magnitude_at_least" or "vector2_magnitude" => DerivedTriggerKind.Vector2MagnitudeAtLeast,
            "axismagnitudeatleast" or "axis_magnitude_at_least" or "axis_magnitude" => DerivedTriggerKind.AxisMagnitudeAtLeast,
            _ => DerivedTriggerKind.Vector2MagnitudeAtLeast
        };
    }

    private static DerivedTriggerMode ParseDerivedTriggerMode(string? mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "hold" => DerivedTriggerMode.Hold,
            _ => DerivedTriggerMode.Hold
        };
    }
}

// JSON schema classes
public class InputConfig
{
    public List<ActionMapConfig>? ActionMaps { get; set; }
}

public class ActionMapConfig
{
    public string? Name { get; set; }
    public List<ActionConfig>? Actions { get; set; }
}

public class ActionConfig
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public float? DeadZone { get; set; }
    public List<string>? Bindings { get; set; }
    public List<CompositeConfig>? Composites { get; set; }
    public List<DerivedTriggerConfig>? DerivedTriggers { get; set; }
}

public class CompositeConfig
{
    public string? Name { get; set; }
    public string? Up { get; set; }
    public string? Down { get; set; }
    public string? Left { get; set; }
    public string? Right { get; set; }
}

// Source generator for AOT-compatible JSON serialization
[JsonSerializable(typeof(InputConfig))]
[JsonSerializable(typeof(ActionMapConfig))]
[JsonSerializable(typeof(ActionConfig))]
[JsonSerializable(typeof(CompositeConfig))]
[JsonSerializable(typeof(DerivedTriggerConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class InputConfigJsonContext : JsonSerializerContext
{
}
