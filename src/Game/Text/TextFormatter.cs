using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using UAlbion.Api;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.State;

namespace UAlbion.Game.Text;

public class TextFormatter : GameServiceComponent<ITextFormatter>, ITextFormatter
{
    public ITextFormatter NoWrap() => new CustomisedTextFormatter(this).NoWrap();
    public ITextFormatter Left() => new CustomisedTextFormatter(this).Left();
    public ITextFormatter Center() => new CustomisedTextFormatter(this).Center();
    public ITextFormatter Right() => new CustomisedTextFormatter(this).Right();
    public ITextFormatter Justify() => new CustomisedTextFormatter(this).Justify();
    public ITextFormatter Fat() => new CustomisedTextFormatter(this).Fat();
    public ITextFormatter Ink(InkId id) => new CustomisedTextFormatter(this).Ink(id);
    public ITextFormatter Block(BlockId blockId) => new CustomisedTextFormatter(this).Block(blockId);

    IEnumerable<(Token, object)> Substitute(
        IAssetManager assets,
        IEnumerable<(Token, object)> tokens,
        object[] args)
    {
        object active = null;
        int argNumber = 0;
        foreach(var (token, p) in tokens)
        {
            switch(token)
            {
                // Defensive substitutions — these tokens appear in a handful of templates
                // and previously threw NotImplementedException, crashing any text that
                // used them. Damage takes the next numeric arg (or "?" when absent); Me
                // names the active character (or the leader). The arg-order model is a
                // best-effort fallback (the original's exact token-arg binding wasn't
                // decoded); it never crashes, and no live text was observed to mis-render.
                case Token.Damage:
                    yield return (Token.Text, argNumber < args.Length
                        ? args[argNumber++]?.ToString() ?? "?"
                        : "?");
                    break;

                case Token.Me:
                    yield return SubstituteName(active ?? TryResolve<IParty>()?.Leader?.Effective);
                    break;

                case Token.Class:
                    foreach (var valueTuple in SubstituteClass(assets, active))
                        yield return valueTuple;
                    break;

                case Token.He:
                case Token.Him:
                case Token.His:
                    foreach (var valueTuple1 in SubstitutePronoun(assets, active, token))
                        yield return valueTuple1;
                    break;

                case Token.Name:
                    yield return SubstituteName(active);
                    break;

                case Token.Price:
                    yield return SubstitutePrice(active);
                    break;

                case Token.Race:
                    foreach (var valueTuple2 in SubstituteRace(assets, active))
                        yield return valueTuple2;
                    break;

                case Token.Sex:
                    foreach (var valueTuple3 in SubstituteSex(active))
                        yield return valueTuple3;
                    break;

                // Change context. A non-null payload (passed via implicitTokens) overrides the
                // GameState lookup so callers can seed the active entity directly — e.g. combat
                // messages naming a specific combatant whose {NAME} would otherwise be unresolved.
                case Token.Combatant: active = p ?? Resolve<IGameState>().Combatant; break;
                case Token.Inventory: active = p ?? Resolve<IGameState>().CurrentInventory; break;
                case Token.Leader: active = p ?? Resolve<IGameState>().Leader; break;
                case Token.Subject: active = p ?? Resolve<IGameState>().Subject; break;
                case Token.Victim: active = p ?? Resolve<IGameState>().Victim; break;
                case Token.Weapon: active = p ?? Resolve<IGameState>().Weapon; break;

                case Token.Parameter:
                    // TXT-02: degrade gracefully when a %s/%d/%u placeholder has no matching arg
                    // (or a null arg), mirroring the hardened {DAMG} case above — was an
                    // unchecked args[argNumber].ToString() that threw on too-few/ null args.
                    yield return (Token.Text, argNumber < args.Length
                        ? FormatParameterArg(assets, args[argNumber++])
                        : "?");
                    break;

                default: yield return (token, p); break;
            }
        }
    }

    // %s args can be rich objects (the "X is broken!" message passes the ItemData):
    // render their display name like the original, not the debug ToString().
    string FormatParameterArg(IAssetManager assets, object arg) => arg switch
    {
        null => "?",
        ItemData item => assets.LoadStringSafe(item.Name),
        ICharacterSheet sheet => sheet.GetName(ReadVar(V.User.Gameplay.Language)),
        _ => arg.ToString(),
    };

    static IEnumerable<(Token, object)> SubstituteSex(object active)
    {
        if (active is not ICharacterSheet character)
        {
            yield return (Token.Text, "{SEX}");
            yield break;
        }

        switch (character.Gender)
        {
            case Gender.Male:
                yield return (Token.Text, "♂");
                break;
            case Gender.Female:
                yield return (Token.Text, "♀");
                break;
        }
    }

    static IEnumerable<(Token, object)> SubstituteRace(IAssetManager assets, object active)
    {
        if (active is not ICharacterSheet character)
        {
            yield return (Token.Text, "{RACE}");
            yield break;
        }

        switch (character.Race)
        {
            case PlayerRace.Terran:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_Terran));
                break;
            case PlayerRace.Iskai:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_Iskai));
                break;
            case PlayerRace.Celt:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_Celt));
                break;
            case PlayerRace.KengetKamulos:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_KengetKamulos));
                break;
            case PlayerRace.DjiCantos:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_DjiCantos));
                break;
            case PlayerRace.Mahino:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_Mahino));
                break;
            case PlayerRace.Decadent:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_Decadent));
                break;
            case PlayerRace.Umajo:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_Umajo));
                break;
            case PlayerRace.Monster:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Race_Monster));
                break;
            default:
                throw new InvalidEnumArgumentException(nameof(character.Race), (int)character.Race, typeof(PlayerRace));
        }
    }

    // Gold is stored in tenths; the original renders prices as "<whole>.<tenth>" with a
    // hardcoded '.' in every language (no currency symbol) — so no i18n separator needed.
    static (Token, object) SubstitutePrice(object active) =>
        active is not ItemData item
            ? (Token.Text, "{PRIC}")
            : (Token.Text, $"{item.Value / 10}.{item.Value % 10}");

    (Token, object) SubstituteName(object active)
    {
        switch (active)
        {
            case ICharacterSheet character:
            {
                var language = ReadVar(V.User.Gameplay.Language);
                return (Token.Text, character.GetName(language));
            }
            case ItemData item: return (Token.Text, item.Name);
            default: return (Token.Text, "{NAME}");
        }
    }

    static IEnumerable<(Token, object)> SubstitutePronoun(IAssetManager assets, object active, Token token)
    {
        if (active is not ICharacterSheet character)
        {
            yield return (Token.Text, $"{{{token}}}");
            yield break;
        }

        var word = (token, character.Gender) switch
        {
            (Token.He, Gender.Male) => Base.SystemText.Meta_He,
            (Token.He, Gender.Female) => Base.SystemText.Meta_She,
            (Token.He, Gender.Neuter) => Base.SystemText.Meta_ItNominative,
            (Token.Him, Gender.Male) => Base.SystemText.Meta_HimAccusative,
            (Token.Him, Gender.Female) => Base.SystemText.Meta_HerAccusative,
            (Token.Him, Gender.Neuter) => Base.SystemText.Meta_ItAccusative,
            (Token.His, Gender.Male) => Base.SystemText.Meta_His,
            (Token.His, Gender.Female) => Base.SystemText.Meta_Her,
            (Token.His, Gender.Neuter) => Base.SystemText.Meta_Its,
            _ => throw new NotImplementedException()
        };

        yield return (Token.Text, assets.LoadStringSafe(word));
    }

    static IEnumerable<(Token, object)> SubstituteClass(IAssetManager assets, object active)
    {
        if (active is not ICharacterSheet character)
        {
            yield return (Token.Text, "{CLAS}");
            yield break;
        }

        switch (character.PlayerClass)
        {
            case PlayerClass.Pilot:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_Pilot));
                break;
            case PlayerClass.Scientist:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_Scientist));
                break;
            case PlayerClass.IskaiWarrior:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_Warrior));
                break;
            case PlayerClass.DjiKasMage:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_DjiKasMage));
                break;
            case PlayerClass.Druid:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_Druid));
                break;
            case PlayerClass.EnlightenedOne:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_EnlightenedOne));
                break;
            case PlayerClass.Technician:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_Technician));
                break;
            case PlayerClass.OquloKamulos:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_OquloKamulos));
                break;
            case PlayerClass.Warrior:
                yield return (Token.Text, assets.LoadStringSafe(Base.SystemText.Class_Warrior2));
                break;
            case PlayerClass.Monster:
                yield return (Token.Text, "Monster");
                break;
            default:
                throw new InvalidEnumArgumentException(nameof(character.PlayerClass), (int)character.PlayerClass,
                    typeof(PlayerClass));
        }
    }

    static IEnumerable<TextBlock> TokensToBlocks(IWordLookup wordLookup, IEnumerable<(Token, object)> tokens, string raw)
    {
        var sb = new StringBuilder();
        var block = new TextBlock { Raw = raw };

        foreach (var (token, p) in tokens)
        {
            if (sb.Length > 0 && (token != Token.Text && token != Token.Word) || token == Token.Block)
            {
                block.Text = sb.ToString();
                yield return block;

                sb.Clear();
                int blockId = token == Token.Block ? (int)p : (int)block.BlockId;
                block = new TextBlock((BlockId)blockId)
                {
                    Alignment = block.Alignment,
                    Style = block.Style,
                    InkId = block.InkId,
                    Raw = raw
                };
            }

            switch (token)
            {
                case Token.Ink: block.InkId = (InkId)p; break;

                case Token.NormalSize: block.Style = TextStyle.Normal; break;
                case Token.Big: block.Style = TextStyle.Big; break;
                case Token.Fat: block.Style = TextStyle.Fat; break;
                case Token.FatHigh: block.Style = TextStyle.FatAndHigh; break;
                case Token.High: block.Style = TextStyle.High; break;

                case Token.Left: block.Alignment = TextAlignment.Left; break;
                case Token.Centre: block.Alignment = TextAlignment.Center; break;
                case Token.Right: block.Alignment = TextAlignment.Right; break;
                case Token.Justify: block.Alignment = TextAlignment.Justified; break;

                case Token.NewLine: block.ArrangementFlags |= TextArrangementFlags.ForceNewLine; break;
                case Token.NoWrap: block.ArrangementFlags |= TextArrangementFlags.NoWrap; break;

                case Token.Text:
                    sb.Append((string) p);
                    break;

                case Token.Block: break; // Handled above
                case Token.Tecf: break; // ???

                case Token.Word:
                {
                    WordId word = wordLookup.Parse((string)p);
                    if (word.IsNone)
                        sb.Append((string) p);
                    else
                        block.AddWord(word);
                    break;
                }
            }
        }

        if (sb.Length > 0)
        {
            block.Text = sb.ToString();
            yield return block;
        }
    }

    IEnumerable<TextBlock> InnerFormat(string template, object[] arguments, IList<(Token, object)> implicitTokens, IAssetManager assets)
    {
        PerfTracker.IncrementFrameCounter("Format text");

        var tokens = Tokeniser.Tokenise(template);
        if (implicitTokens != null)
            tokens = implicitTokens.Concat(tokens);

        IEnumerable<(Token, object)> substituted = Substitute(assets, tokens, arguments);

#if DEBUG
        substituted = substituted.ToList();
#endif

        var blocks = TokensToBlocks(Resolve<IWordLookup>(), substituted, template);

#if DEBUG
        blocks = blocks.ToList();
#endif

        return blocks;
    }

    public IText Format(TextId textId, params object[] arguments)
        => Format(textId, null, arguments);

    public IText Format(StringId stringId, params object[] arguments)
        => Format(stringId, null, arguments);

    public IText Format(string templateText, params object[] arguments)
        => Format(templateText, null, arguments);

    public IText Format(TextId textId, IList<(Token, object)> implicitTokens, params object[] arguments)
        => Format(new StringId(textId), implicitTokens, arguments);

    public IText Format(StringId stringId, IList<(Token, object)> implicitTokens, params object[] arguments)
        => new DynamicText(() =>
        {
            string templateText = Assets.LoadStringSafe(stringId);
            return InnerFormat(templateText, arguments, implicitTokens, Assets);
        });

    public IText Format(string templateText, IList<(Token, object)> implicitTokens, params object[] arguments)
        => new DynamicText(() => InnerFormat(templateText, arguments, implicitTokens, Assets));
}
