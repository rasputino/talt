using Silk.NET.Input;

namespace Talt.Client.Input;

/// <summary>Whether an action fires once per key press or is active while the key is held.</summary>
internal enum ActionKind
{
    Press,
    Held,
}

/// <summary>A key, optionally with Shift.</summary>
internal readonly record struct KeyChord(Key Key, bool Shift)
{
    /// <summary>Parses "Space" or "Shift+Delete" (Silk.NET.Input.Key names).</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var shift = text.StartsWith("Shift+", StringComparison.Ordinal);
        var name = shift ? text["Shift+".Length..] : text;
        if (!Enum.TryParse<Key>(name, ignoreCase: false, out var key) || key == Key.Unknown || !Enum.IsDefined(key)
            || key is Key.ShiftLeft or Key.ShiftRight)
        {
            return false;
        }

        chord = new KeyChord(key, shift);
        return true;
    }

    public override string ToString() => Shift ? $"Shift+{Key}" : Key.ToString();
}

/// <summary>A bindable action (data-model.md InputAction).</summary>
internal sealed record InputActionDefinition(string Id, string LabelKey, string Category, ActionKind Kind, IReadOnlyList<KeyChord> DefaultKeys);

/// <summary>
/// Data-driven key bindings (FR-027, FR-028): actions and default keys come from
/// <c>Input/Data/actions.json</c>; the player's changes are stored as overrides in the settings.
/// Every action has up to <see cref="SlotsPerAction"/> keys and a key belongs to at most one action.
/// </summary>
internal sealed class KeyBindings
{
    public const int SlotsPerAction = 2;
    private const string FileName = "actions.json";

    private readonly Dictionary<string, KeyChord?[]> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InputActionDefinition> _byId;

    public KeyBindings(IReadOnlyList<InputActionDefinition> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        Actions = actions;
        _byId = actions.ToDictionary(a => a.Id, StringComparer.Ordinal);
        RestoreDefaults();
    }

    /// <summary>Every action, in data-file order.</summary>
    public IReadOnlyList<InputActionDefinition> Actions { get; }

    /// <summary>Incremented on every change (for texts that show keys).</summary>
    public int Version { get; private set; }

    /// <summary>Loads the embedded action definitions.</summary>
    public static IReadOnlyList<InputActionDefinition> LoadDefinitions()
    {
        var data = DataFiles.Load<ActionsData>(FileName);
        var list = new List<InputActionDefinition>();
        foreach (var a in data.Actions ?? throw DataFiles.Invalid(FileName, "missing 'actions'."))
        {
            if (string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Label) || string.IsNullOrWhiteSpace(a.Category))
            {
                throw DataFiles.Invalid(FileName, "every action needs 'id', 'label' and 'category'.");
            }

            if (list.Any(existing => existing.Id == a.Id))
            {
                throw DataFiles.Invalid(FileName, $"action '{a.Id}' is listed twice.");
            }

            var kind = a.Kind switch
            {
                "press" => ActionKind.Press,
                "held" => ActionKind.Held,
                _ => throw DataFiles.Invalid(FileName, $"action '{a.Id}' has kind '{a.Kind}'; use 'press' or 'held'."),
            };
            var keys = new List<KeyChord>();
            foreach (var text in a.Keys ?? [])
            {
                if (!KeyChord.TryParse(text, out var chord))
                {
                    throw DataFiles.Invalid(FileName, $"action '{a.Id}' has an unknown key '{text}'.");
                }

                keys.Add(chord);
            }

            if (keys.Count > SlotsPerAction)
            {
                throw DataFiles.Invalid(FileName, $"action '{a.Id}' has more than {SlotsPerAction} keys.");
            }

            list.Add(new InputActionDefinition(a.Id, a.Label, a.Category, kind, keys));
        }

        return list;
    }

    public InputActionDefinition? Find(string actionId) => _byId.GetValueOrDefault(actionId);

    /// <summary>The key in <paramref name="slot"/> of an action, or null when empty.</summary>
    public KeyChord? Get(string actionId, int slot) =>
        _bindings.TryGetValue(actionId, out var keys) && (uint)slot < SlotsPerAction ? keys[slot] : null;

    /// <summary>First bound key of an action, for tooltips.</summary>
    public KeyChord? Primary(string actionId) => Get(actionId, 0) ?? Get(actionId, 1);

    /// <summary>
    /// The press action for a key. An exact match (including Shift) wins; otherwise a binding without
    /// Shift also answers when Shift is held (Shift then modifies the action, e.g. a larger terrain edit).
    /// </summary>
    public bool TryGetAction(Key key, bool shift, out string actionId)
    {
        if (TryFind(new KeyChord(key, shift), out actionId))
        {
            return true;
        }

        return shift && TryFind(new KeyChord(key, false), out actionId);
    }

    /// <summary>Whether any key of a held action is down.</summary>
    public bool IsHeld(string actionId, Func<Key, bool> isKeyDown, bool shiftDown = false)
    {
        ArgumentNullException.ThrowIfNull(isKeyDown);
        if (!_bindings.TryGetValue(actionId, out var keys))
        {
            return false;
        }

        foreach (var chord in keys)
        {
            if (chord is { } c && isKeyDown(c.Key) && (!c.Shift || shiftDown))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The action (other than <paramref name="exceptAction"/>) that already uses a key, if any.</summary>
    public string? FindConflict(KeyChord chord, string exceptAction)
    {
        foreach (var (id, keys) in _bindings)
        {
            if (id != exceptAction && Array.IndexOf(keys, chord) >= 0)
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>Binds a key to a slot; any other action using that key loses it (conflict confirmed).</summary>
    public void Set(string actionId, int slot, KeyChord? chord)
    {
        if (!_bindings.TryGetValue(actionId, out var keys) || (uint)slot >= SlotsPerAction)
        {
            throw new ArgumentException($"Unknown action '{actionId}' or slot {slot}.", nameof(actionId));
        }

        if (chord is { } c)
        {
            foreach (var other in _bindings.Values)
            {
                for (var i = 0; i < SlotsPerAction; i++)
                {
                    if (other[i] == c)
                    {
                        other[i] = null;
                    }
                }
            }
        }

        keys[slot] = chord;
        Version++;
    }

    public void RestoreDefaults()
    {
        _bindings.Clear();
        foreach (var action in Actions)
        {
            var keys = new KeyChord?[SlotsPerAction];
            for (var i = 0; i < action.DefaultKeys.Count; i++)
            {
                keys[i] = action.DefaultKeys[i];
            }

            _bindings[action.Id] = keys;
        }

        Version++;
    }

    /// <summary>
    /// Applies the player's overrides (data-model.md PlayerSettings.KeyBindings). Unknown actions and keys
    /// are ignored and reported through <paramref name="ignored"/>.
    /// </summary>
    public void ApplyOverrides(IReadOnlyDictionary<string, string[]>? overrides, Action<string>? ignored = null)
    {
        if (overrides is null)
        {
            return;
        }

        foreach (var (id, keys) in overrides)
        {
            if (!_bindings.ContainsKey(id))
            {
                ignored?.Invoke($"unknown action '{id}'");
                continue;
            }

            for (var slot = 0; slot < SlotsPerAction; slot++)
            {
                var text = slot < keys.Length ? keys[slot] : null;
                if (string.IsNullOrEmpty(text))
                {
                    Set(id, slot, null);
                }
                else if (KeyChord.TryParse(text, out var chord))
                {
                    Set(id, slot, chord);
                }
                else
                {
                    ignored?.Invoke($"unknown key '{text}' for '{id}'");
                }
            }
        }
    }

    /// <summary>The bindings that differ from the defaults, for saving.</summary>
    public Dictionary<string, string[]> Overrides()
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var action in Actions)
        {
            var keys = _bindings[action.Id];
            var differs = false;
            for (var i = 0; i < SlotsPerAction; i++)
            {
                KeyChord? expected = i < action.DefaultKeys.Count ? action.DefaultKeys[i] : null;
                differs |= keys[i] != expected;
            }

            if (differs)
            {
                result[action.Id] = keys.Select(k => k?.ToString() ?? string.Empty).ToArray();
            }
        }

        return result;
    }

    private bool TryFind(KeyChord chord, out string actionId)
    {
        foreach (var action in Actions)
        {
            if (action.Kind == ActionKind.Press && Array.IndexOf(_bindings[action.Id], chord) >= 0)
            {
                actionId = action.Id;
                return true;
            }
        }

        actionId = string.Empty;
        return false;
    }

    /// <summary>JSON shape of actions.json.</summary>
    internal sealed class ActionsData
    {
        public List<ActionData>? Actions { get; set; }
    }

    internal sealed class ActionData
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string[]? Keys { get; set; }
    }
}
