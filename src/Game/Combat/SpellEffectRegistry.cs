using System.Collections.Generic;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.Combat;

/// <summary>
/// Global registry of <see cref="ISpellEffect"/> handlers, keyed by <see cref="SpellId"/>.
/// Until per-spell logic is reverse-engineered, unregistered
/// spells fall through with <see cref="SpellCastOutcome.Failed"/>.
/// </summary>
public static class SpellEffectRegistry
{
    static readonly Dictionary<SpellId, ISpellEffect> Handlers = [];

    public static void Register(ISpellEffect effect)
    {
        if (effect == null) return;
        Handlers[effect.SpellId] = effect;
    }

    public static bool TryGet(SpellId id, out ISpellEffect effect)
        => Handlers.TryGetValue(id, out effect);

    public static SpellCastOutcome Cast(SpellId id, SpellCastContext context)
    {
        if (Handlers.TryGetValue(id, out var effect))
            return effect.Apply(context);
        return SpellCastOutcome.Failed;
    }

    public static int RegisteredCount => Handlers.Count;

    /// <summary>Clears the registry. Used by tests.</summary>
    public static void Clear() => Handlers.Clear();
}
