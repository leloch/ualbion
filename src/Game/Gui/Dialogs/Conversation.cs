using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Config;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Events.Transitions;
using UAlbion.Game.State;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Dialogs;

public class Conversation : GameComponent
{
    static readonly Vector2 ConversationPositionLeft = new(20, 20); // For 'give item' transitions
    static readonly Vector2 ConversationPositionRight = new(335, 20);

    readonly PartyMemberId _partyMemberId;
    readonly ICharacterSheet _npc;
    readonly Dictionary<WordId, WordStatus> _topics = [];

    ITextFormatter _tf;
    ConversationTextWindow _textWindow;
    ConversationTopicWindow _topicsWindow;
    ConversationOptionsWindow _optionsWindow;
    ConversationItemWindow _itemWindow;
    bool _done;

    public Conversation(PartyMemberId partyMemberId, ICharacterSheet npc)
    {
        On<EndDialogueEvent>(_ => Close());
        On<UnloadMapEvent>(_ => Close());
        On<DataChangeEvent>(OnDataChange);

        _partyMemberId = partyMemberId;
        _npc = npc ?? throw new ArgumentNullException(nameof(npc));
    }

    void Close() => _done = true;

    protected override void Subscribed()
    {
        Raise(new PushInputModeEvent(InputMode.Conversation));

        _tf = Resolve<ITextFormatter>();
        var game = TryResolve<IGameState>();
        var dialogs = Resolve<IDialogManager>();
        var sheet = game?.GetSheet(_partyMemberId.ToSheet()) ?? Assets.LoadSheet(_partyMemberId.ToSheet());

        // Seed this conversation's topic list with every keyword the party has already
        // discovered (from any NPC) so words carry across the whole roster, not just within
        // one chat. New words found here are shared back via DiscoverTopics → DiscoverWord.
        if (game != null)
            foreach (var word in game.DiscoveredWords)
                _topics.TryAdd(word, WordStatus.Mentioned);

        AttachChild(new ConversationParticipantLabel(sheet, false));
        AttachChild(new ConversationParticipantLabel(_npc, true));

        _textWindow = dialogs.AddDialog(depth => new ConversationTextWindow(depth));
        _optionsWindow = dialogs.AddDialog(depth => new ConversationOptionsWindow(depth) { IsActive = false });
        _topicsWindow = dialogs.AddDialog(depth => new ConversationTopicWindow(depth) { IsActive = false });
        _itemWindow = dialogs.AddDialog(depth => new ConversationItemWindow(depth) { IsActive = false });
    }

    (IText, BlockId?, BlockId)[] BuildStandardOptions()
    {
        var options = new List<(IText, BlockId?, BlockId)>
        {
            (_tf.Format(Base.SystemText.Dialog_WhatsYourProfession), null, BlockId.Profession),
            (_tf.Format(Base.SystemText.Dialog_WhatDoYouKnowAbout), null, BlockId.QueryWord),
            (_tf.Format(Base.SystemText.Dialog_WhatDoYouKnowAboutThisItem), null, BlockId.QueryItem),
        };

        // Recruitment / dismissal are offered only when the NPC's set actually has the chain
        // (ActionType 0x4 / 0x5). The chain bodies do the real add/remove_party_member.
        if (HasAction(ActionType.AskToJoin))
            options.Add((_tf.Format(Base.SystemText.Dialog_WouldYouLikeToJoinUs), null, BlockId.AskToJoin));
        if (HasAction(ActionType.AskToLeave))
            options.Add((_tf.Format(Base.SystemText.Dialog_IWouldLikeYouToLeaveUs), null, BlockId.AskToLeave));

        options.Add((_tf.Format(Base.SystemText.Dialog_ItsBeenNiceTalkingToYou), null, BlockId.Farewell));
        return options.ToArray();
    }

    // True when the conversing NPC's event set (or word set) has a chain headed by this
    // action type with the default block/argument (AskToJoin/AskToLeave carry neither).
    bool HasAction(ActionType type)
    {
        if (!_npc.EventSetId.IsNone && Assets.LoadEventSet(_npc.EventSetId) is { } set
            && FindActionChain(set, type, 0, AssetId.None) != null)
            return true;
        if (!_npc.WordSetId.IsNone && Assets.LoadEventSet(_npc.WordSetId) is { } wordSet
            && FindActionChain(wordSet, type, 0, AssetId.None) != null)
            return true;
        return false;
    }

    public async AlbionTask Run()
    {
        await TriggerAction(ActionType.StartDialogue, 0, AssetId.None);
        var standardOptions = BuildStandardOptions();

        while (!_done)
        {
            if (_optionsWindow.IsActive || !IsSubscribed)
                return;

            var blockId = await _optionsWindow.GetOption(null, standardOptions);
            if (await BlockClicked(blockId))
                break;
        }
    }

    async AlbionTask<bool> BlockClicked(BlockId blockId, ushort promptNumber = 0) // return true if conversation is complete
    {
        switch (blockId)
        {
            case BlockId.Profession:
                {
                    var setId = _npc.EventSetId.ToEventText();
                    var strings = (IStringSet)Resolve<IModApplier>().LoadAssetCached(setId);

                    ushort subId = 0;
                    for (ushort i = 0; i < strings.Count; i++)
                    {
                        var s = strings.GetString(new StringId(setId, i));
                        if (Tokeniser.Tokenise(s).Any(x => x.Token == Token.Block && x.Argument is 0))
                        {
                            subId = i;
                            break;
                        }
                    }

                    var text = _tf.Ink(Base.Ink.Yellow).Format(new StringId(setId, subId));
                    _textWindow.Show(text, BlockId.Profession);
                    await _textWindow.Closed();
                    break;
                }

            case BlockId.QueryWord:
                {
                    _topicsWindow.IsActive = true;
                    var wordId = await _topicsWindow.GetWord(_topics);

                    if (!wordId.IsNone)
                    {
                        var lookup = Resolve<IWordLookup>();
                        bool triggered = false;
                        foreach (var homonym in lookup.GetHomonyms(wordId))
                        {
                            if (await TriggerWordAction(homonym)) { triggered = true; break; }
                        }
                        // TXT-04: mark the CLICKED topic (its _topics key is wordId, the first
                        // homonym from WordLookup.Parse) as discussed — not the homonym that owned
                        // the chain, which may be a different id and leave the clicked row Mentioned.
                        if (triggered)
                            _topics[wordId] = WordStatus.Discussed;
                    }
                    break;
                }

            case BlockId.QueryItem:
                {
                    // "What do you know about this item?" — pick an item from the talking
                    // member's inventory, then fire the NPC's AskAboutItem action chain.
                    // Without a matching chain the NPC gives the standard brush-off.
                    var game = TryResolve<IGameState>();
                    var sheet = game?.GetSheet(_partyMemberId.ToSheet()) ?? Assets.LoadSheet(_partyMemberId.ToSheet());

                    var seen = new HashSet<ItemId>();
                    var items = new List<(ItemId, IText)>();
                    foreach (var slot in sheet.Inventory.EnumerateAll())
                    {
                        if (slot.Item.IsNone || slot.Item.Type != AssetType.Item || !seen.Add(slot.Item))
                            continue;
                        var item = Assets.LoadItem(slot.Item);
                        if (item == null)
                            continue;
                        items.Add((slot.Item, _tf.NoWrap().Format(item.Name)));
                    }

                    _itemWindow.IsActive = true;
                    var itemId = await _itemWindow.GetItem(items);
                    if (itemId.IsNone)
                        break;

                    // AskAboutItem chains key on (block = item class, 255 = any class) +
                    // the item id — try the item's own class first, then the wildcard.
                    var pickedItem = Assets.LoadItem(itemId);
                    byte itemClass = (byte)(pickedItem?.TypeId ?? 0);
                    bool handled = await TriggerAction(ActionType.AskAboutItem, itemClass, itemId)
                                || await TriggerAction(ActionType.AskAboutItem, 255, itemId)
                                || await TriggerAction(ActionType.AskAboutItem, 0, itemId);

                    if (!handled)
                    {
                        var text = _tf.Ink(Base.Ink.Yellow).Format(Base.SystemText.Dialog_ImNotInterestedInThisItem);
                        _textWindow.Show(text, BlockId.MainText);
                        await _textWindow.Closed();
                    }
                    break;
                }

            case BlockId.AskToJoin:
                {
                    // The AskToJoin chain runs the prompt + add_party_member (and shows the
                    // "party complete" / recruit-response text itself). Only offered when the
                    // chain exists, so a false return here is unexpected — log via the default.
                    if (!await TriggerAction(ActionType.AskToJoin, 0, AssetId.None))
                        Warn("Conversation: AskToJoin offered but no chain ran");
                    break;
                }

            case BlockId.AskToLeave:
                {
                    // The AskToLeave chain runs the prompt + remove_party_member (which clears
                    // the member's home-NPC RemovedNpcs bit — see Party.OnRemovePartyMember).
                    if (!await TriggerAction(ActionType.AskToLeave, 0, AssetId.None))
                        Warn("Conversation: AskToLeave offered but no chain ran");
                    break;
                }

            case BlockId.Farewell:
                {
                    if (await TriggerAction(ActionType.FinishDialogue, 0, AssetId.None))
                        return true; // If there was a custom finish-dialogue script then we don't need to show the default message

                    var text = _tf.Ink(Base.Ink.Yellow).Format(Base.SystemText.Dialog_Farewell);
                    _textWindow.Show(text, BlockId.MainText);
                    await _textWindow.Closed();
                    return true;
                }

            default:
                {
                    // Numbered conversation options (regular block ids start at 10) dispatch a
                    // DialogueLine action keyed by (block, prompt-number). The prompt number is
                    // the SubId of the text event that presented the options — RE'd shape:
                    // `action DialogueLine <block> PromptNumber.<subId>` (AlbionDecompilerTests).
                    // These chains carry the branching dialogue, recruitment (add_party_member
                    // behind a prompt_player), quest beats, etc. — the bulk of conversation flow.
                    if ((int)blockId >= 10)
                    {
                        var promptArg = new AssetId(AssetType.PromptNumber, promptNumber);
                        if (!await TriggerAction(ActionType.DialogueLine, (byte)blockId, promptArg))
                            Warn($"Conversation: no DialogueLine chain for block {(int)blockId} prompt {promptNumber}");
                        break;
                    }

                    // MainText (-1) is a UiText filtering pseudo-id never dispatched here.
                    // Anything else is malformed data.
                    Warn($"Conversation: unhandled block id {blockId}");
                    break;
                }
        }

        return false;
    }

    protected override void Unsubscribed()
    {
        _textWindow.Remove();
        _topicsWindow.Remove();
        _optionsWindow.Remove();

        _textWindow = null;
        _topicsWindow = null;
        _optionsWindow = null;

        Raise(new PopInputModeEvent());
    }

    void DiscoverTopics(IEnumerable<WordId> topics)
    {
        var state = TryResolve<IGameState>();
        foreach (var topic in topics)
        {
            if (!_topics.TryGetValue(topic, out var currentStatus) || currentStatus == WordStatus.Unknown)
                _topics[topic] = WordStatus.Mentioned;
            // Share the discovery across NPCs so the word stays askable elsewhere.
            state?.DiscoverWord(topic);
        }
    }

    public async AlbionTask OnText(TextEvent mapTextEvent)
    {
        ArgumentNullException.ThrowIfNull(mapTextEvent);

        switch (mapTextEvent.Location)
        {
            case TextLocation.Conversation:
            case TextLocation.NoPortrait:
                {
                    var text = _tf.Ink(Base.Ink.Yellow).Format(mapTextEvent.ToId(_npc.EventSetId.ToEventText()));
                    var topics = text.GetBlocks().SelectMany(x => x.Words);
                    DiscoverTopics(topics);
                    _textWindow.Show(text, null);
                    await _textWindow.Closed();
                    return;
                }

            case TextLocation.ConversationOptions:
                {
                    var text = _tf.Ink(Base.Ink.Yellow).Format(mapTextEvent.ToId(_npc.EventSetId.ToEventText()));
                    DiscoverTopics(text.GetBlocks().SelectMany(x => x.Words));
                    // Show ONLY the NPC's spoken line (BlockId.MainText) in the text window. A null
                    // filter rendered EVERY block — including the numbered response blocks (>0) that
                    // belong in the options window — so the player's choices were duplicated into the
                    // NPC popup (the reported "answers appear in the dialogue text" repetition).
                    _textWindow.Show(text, BlockId.MainText);
                    // Note: Not waiting for the user to click, as this should be the same text as the last event, but with options

                    var options = text.GetBlocks()
                        .Where(x => x.BlockId > 0)
                        .Select(x => x.BlockId)
                        .Distinct()
                        .Select(x => (text, (BlockId?)x, x))
                        .ToArray();

                    var standardOptions = BuildStandardOptions();

                    var blockId = await _optionsWindow.GetOption(options, standardOptions);
                    if (await BlockClicked(blockId, mapTextEvent.SubId))
                        Close();

                    return;
                }

            case TextLocation.ConversationQuery:
                {
                    var text = _tf.Ink(Base.Ink.Yellow).Format(mapTextEvent.ToId(_npc.EventSetId.ToEventText()));

                    DiscoverTopics(text.GetBlocks().SelectMany(x => x.Words));

                    // MainText only — keep the numbered response blocks out of the NPC text popup
                    // (they're shown as selectable options below).
                    _textWindow.Show(text, BlockId.MainText);
                    await _textWindow.Closed();

                    var options = text.GetBlocks()
                        .Where(x => x.BlockId > 0)
                        .Select(x => x.BlockId)
                        .Distinct()
                        .Select(x => (text, (BlockId?)x, x))
                        .ToArray();

                    var blockId = await _optionsWindow.GetOption(options, null);
                    if (await BlockClicked(blockId, mapTextEvent.SubId))
                        Close();

                    break;
                }

            case TextLocation.StandardOptions:
                {
                    // In the original game the StandardOptions text event doesn't block the chain:
                    // the chain continues (showing the greeting text etc.) and the standard options
                    // menu is presented afterwards (see Run()'s loop). Blocking here swallowed the
                    // player's first click and hid the greeting until after a click.
                    return;
                }
        }

        // Actions to check: StartDialogue, DialogueLine #,#, DialogueLine WORD, EndDialogue

        /*
            Enumerable.Empty<(IText, IEvent)>(), true
            ));

        if(addStandardOptions)
        {
        }
        */
    }

    void OnDataChange(IDataChangeEvent e) // Handle item transitions when the party receives items
    {
        if (e is not ChangeItemEvent { Operation: NumericOperation.AddAmount } cie)
            return;

        var transitionEvent = new LinearItemTransitionEvent(cie.ItemId,
            (int)ConversationPositionRight.X,
            (int)ConversationPositionRight.Y,
            (int)ConversationPositionLeft.X,
            (int)ConversationPositionLeft.Y,
            null);
        Raise(transitionEvent);
    }

    async AlbionTask<bool> TriggerWordAction(WordId wordId)
    {
        // TXT-04: the "Discussed" mark is applied by the caller against the CLICKED topic key, not
        // here against the (possibly different) homonym that owned the chain.
        return await TriggerAction(ActionType.Word, 0, wordId);
    }

    static ushort? FindActionChain(EventSet set, ActionType type, byte block, AssetId argument)
    {
        foreach (var x in set.Chains)
        {
            if (set.Events[x].Event is not ActionEvent action)
                continue;

            // Wildcards (RE _RE_STORY_ACTIONS.md): Block 255 = "any" (the field comment notes it),
            // and Argument with raw id 32000 (0x7D00) = "any". The matcher previously required exact
            // block+argument, so wildcard chains (e.g. the Tom endgame fallback) never fired.
            const int ArgumentWildcard = 32000;
            if (action.ActionType == type
                && (action.Block == block || action.Block == 255)
                && (action.Argument == argument || action.Argument.Id == ArgumentWildcard))
            {
                return x;
            }
        }

        return null;
    }

    async AlbionTask<bool> TriggerAction(ActionType type, byte small, AssetId argument) // Return true if a script was run for the action
    {
        var chainSource = _npc.EventSetId.IsNone ? null : Assets.LoadEventSet(_npc.EventSetId);
        ushort? eventIndex = null;

        if (chainSource != null)
            eventIndex = FindActionChain(chainSource, type, small, argument);

        if (eventIndex == null) // Fall back to the word set
        {
            chainSource = _npc.WordSetId.IsNone ? null : Assets.LoadEventSet(_npc.WordSetId);
            if (chainSource != null)
                eventIndex = FindActionChain(chainSource, type, small, argument);
        }

        if (eventIndex == null)
            return false;

        var triggerEvent = new TriggerChainEvent(
            chainSource,
            eventIndex.Value,
            new EventSource(chainSource.Id, TriggerType.Action));

        await RaiseA(triggerEvent);

        var action = (ActionEvent)chainSource.Events[eventIndex.Value].Event;
        await RaiseA(new EventVisitedEvent(chainSource.Id, action));
        return true;
    }
}
