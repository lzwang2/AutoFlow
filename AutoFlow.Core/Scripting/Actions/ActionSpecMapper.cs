using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Actions;

/// <summary>
/// Raised when an <see cref="ActionSpec"/> cannot be converted to an <see cref="Instruction"/>.
/// Mapping never drops fields silently — an unknown or unconvertible parameter is an error,
/// because silently ignoring it would produce an action that looks fine but behaves wrong.
/// </summary>
public sealed class ActionMappingException : Exception
{
    public ActionMappingException(string message) : base(message) { }
}

/// <summary>
/// Converts between the execution-time <see cref="Instruction"/> and the serialization-form
/// <see cref="ActionSpec"/>.
///
/// Parameter keys are exactly Instruction's property names. Only fields meaningful for a
/// given CommandType are carried: a Click has no TemplatePath, so storing one would recreate
/// the very field-soup ActionSpec exists to avoid. Fields irrelevant to the type are dropped
/// by design — that is not "losing data", it is discarding values the handler would never read.
/// </summary>
public static class ActionSpecMapper
{
    /// <summary>Fields every command type carries.</summary>
    private static readonly string[] CommonFields = { nameof(Instruction.IntervalToNextInstructionMs) };

    /// <summary>
    /// Per-type parameter names. Derived from what each handler actually reads, so the lists
    /// stay honest: e.g. WaitImage omits PositionX/Y because ClickAfterMatch clicks the match
    /// centre, not a fixed coordinate (see WaitImageHandler).
    /// </summary>
    private static readonly Dictionary<CommandType, string[]> FieldsByType = new()
    {
        [CommandType.Click] = new[]
        {
            nameof(Instruction.PositionX),
            nameof(Instruction.PositionY),
            nameof(Instruction.MouseButton),
            nameof(Instruction.ClickCount)
        },
        [CommandType.WaitImage] = new[]
        {
            nameof(Instruction.TemplatePath),
            nameof(Instruction.MatchThreshold),
            nameof(Instruction.MeanDiffThreshold),
            nameof(Instruction.WaitTimeoutMs),
            nameof(Instruction.ClickAfterMatch)
        },
        [CommandType.WaitColor] = new[]
        {
            nameof(Instruction.PositionX),
            nameof(Instruction.PositionY),
            nameof(Instruction.WaitColorHex),
            nameof(Instruction.WaitColorTolerance),
            nameof(Instruction.WaitTimeoutMs)
        },
        [CommandType.WaitTime] = new[]
        {
            nameof(Instruction.WaitTimeMilliseconds)
        },
        [CommandType.Loop] = new[]
        {
            nameof(Instruction.LoopBackToIndex),
            nameof(Instruction.LoopCount)
        },
        [CommandType.CallSkill] = new[]
        {
            nameof(Instruction.SkillName),
            nameof(Instruction.SkillParameters)
        },
        [CommandType.Comment] = new[]
        {
            nameof(Instruction.CommentContent)
        }
    };

    /// <summary>
    /// Parameter key used to point an action at a shared condition instead of an inline
    /// TemplatePath. Recognised for condition-style actions (WaitImage).
    /// </summary>
    public const string ConditionRefKey = "ConditionRef";

    public static IReadOnlyDictionary<CommandType, string[]> KnownFields => FieldsByType;

    public static ActionSpec ToActionSpec(Instruction instruction, string? id = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        var spec = new ActionSpec
        {
            Id = id,
            Name = name,
            Type = instruction.CommandType,
            Notes = instruction.Notes
        };

        foreach (var field in CommonFields.Concat(FieldsFor(instruction.CommandType)))
        {
            var property = typeof(Instruction).GetProperty(field)
                ?? throw new ActionMappingException(
                    $"内部错误: Instruction 没有属性 '{field}'，字段映射表与类定义不一致。");

            spec.Parameters[field] = property.GetValue(instruction);
        }

        return spec;
    }

    public static Instruction ToInstruction(ActionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var instruction = new Instruction
        {
            CommandType = spec.Type,
            Notes = spec.Notes
        };

        var allowed = new HashSet<string>(
            CommonFields.Concat(FieldsFor(spec.Type)), StringComparer.Ordinal);

        foreach (var (key, value) in spec.Parameters)
        {
            // A condition reference is resolved by the interpreter, not copied onto the
            // instruction, so it is accepted here without being treated as unknown.
            if (string.Equals(key, ConditionRefKey, StringComparison.Ordinal))
                continue;

            if (!allowed.Contains(key))
            {
                throw new ActionMappingException(
                    $"动作 '{Describe(spec)}' 含无法映射的参数 '{key}'。" +
                    $"{spec.Type} 允许的参数: {string.Join(", ", allowed.Order())}。");
            }

            var property = typeof(Instruction).GetProperty(key)
                ?? throw new ActionMappingException(
                    $"内部错误: Instruction 没有属性 '{key}'。");

            if (value is null)
            {
                if (IsNullable(property.PropertyType))
                    property.SetValue(instruction, null);
                // Leaving a non-nullable field at its default is correct: the serializer
                // omits unset values, so null here means "not specified".
                continue;
            }

            try
            {
                property.SetValue(instruction, Coerce(value, property.PropertyType));
            }
            catch (Exception ex) when (ex is not ActionMappingException)
            {
                throw new ActionMappingException(
                    $"动作 '{Describe(spec)}' 的参数 '{key}' 无法转换为 " +
                    $"{property.PropertyType.Name}: 值为 '{value}' ({value.GetType().Name})。{ex.Message}");
            }
        }

        return instruction;
    }

    private static string[] FieldsFor(CommandType type)
        => FieldsByType.TryGetValue(type, out var fields)
            ? fields
            : throw new ActionMappingException($"未知的动作类型 '{type}'，字段映射表需要更新。");

    private static string Describe(ActionSpec spec)
        => !string.IsNullOrWhiteSpace(spec.Name) ? spec.Name!
            : !string.IsNullOrWhiteSpace(spec.Id) ? spec.Id!
            : spec.Type.ToString();

    private static bool IsNullable(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    /// <summary>
    /// Converts a deserialized parameter value to the target property type.
    ///
    /// Necessary because a JSON round-trip does not preserve CLR types: an int comes back as
    /// long, an enum as a string, a nested dictionary as JObject. Without coercion, a spec
    /// that was just written to disk would fail to load back.
    /// </summary>
    private static object? Coerce(object value, Type targetType)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (target.IsInstanceOfType(value) && target != typeof(object))
            return value;

        if (target.IsEnum)
        {
            return value switch
            {
                string text => Enum.Parse(target, text, ignoreCase: true),
                _ => Enum.ToObject(target, Convert.ToInt64(value))
            };
        }

        if (target == typeof(Dictionary<string, object>))
            return CoerceDictionary(value);

        return Convert.ChangeType(value, target);
    }

    private static Dictionary<string, object> CoerceDictionary(object value)
    {
        if (value is Dictionary<string, object> typed)
            return typed;

        // A JSON round-trip turns the nested dictionary into a JObject, which implements
        // IDictionary<string, JToken> rather than the interfaces below — so it must be
        // handled explicitly. Taking the Newtonsoft dependency here is honest: SkillParameters
        // is declared as Dictionary<string, object>, and only the JSON layer can tell us what
        // the untyped values actually were.
        if (value is Newtonsoft.Json.Linq.JObject jObject)
        {
            var converted = jObject.ToObject<Dictionary<string, object>>();
            if (converted is not null)
                return converted;

            throw new ActionMappingException("SkillParameters 的 JSON 对象无法转换为键值结构。");
        }

        if (value is IDictionary<string, object?> loose)
        {
            var result = new Dictionary<string, object>();
            foreach (var (key, item) in loose)
            {
                if (item is not null)
                    result[key] = item;
            }
            return result;
        }

        if (value is System.Collections.IDictionary raw)
        {
            var result = new Dictionary<string, object>();
            foreach (System.Collections.DictionaryEntry entry in raw)
            {
                if (entry.Value is not null)
                    result[Convert.ToString(entry.Key) ?? string.Empty] = entry.Value;
            }
            return result;
        }

        throw new ActionMappingException(
            $"SkillParameters 需要键值结构，实际收到 {value.GetType().Name}。");
    }
}
