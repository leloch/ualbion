using System;
using System.Text.Json;

namespace UAlbion.Api.Settings;

public class BoolVar : IVar<bool>
{
    public BoolVar(VarLibrary library, string key, bool defaultValue)
    {
        ArgumentNullException.ThrowIfNull(library);
        Key = key;
        DefaultValue = defaultValue;
        library.Add(this);
    }

    public string Key { get; }
    public bool DefaultValue { get; }
    public object DefaultValueUntyped => DefaultValue;
    public Type ValueType => typeof(bool);

    public bool Read(IVarSet varSet)
    {
        ArgumentNullException.ThrowIfNull(varSet);
        if (varSet.TryGetValue(Key, out var objValue))
        {
            if (objValue is bool value) return value;
            if (objValue is JsonElement je)
            {
                // A bool persisted to settings.json comes back as a JsonElement with ValueKind
                // True/False (the old code only handled Number - and even then called GetBoolean,
                // which throws on a number - so any saved bool var threw on read, silently
                // disabling every opt-in toggle once it had been saved). Accept numeric 0/1 and
                // "true"/"false" strings too, for leniency.
                switch (je.ValueKind)
                {
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Number: return je.GetInt32() != 0;
                    case JsonValueKind.String when bool.TryParse(je.GetString(), out var b): return b;
                }
            }
            throw new FormatException($"Var {Key} was of unexpected type {objValue.GetType()}, expected bool");
        }

        return DefaultValue;
    }

    public void Write(ISettings varSet, bool value)
    {
        ArgumentNullException.ThrowIfNull(varSet);
        varSet.SetValue(Key, value);
    }

    public void WriteFromString(ISettings varSet, string value)
    {
        var n = bool.Parse(value);
        Write(varSet, n);
    }

    public override string ToString()
        => $"BoolVar({Key}) (default={DefaultValue})";
}