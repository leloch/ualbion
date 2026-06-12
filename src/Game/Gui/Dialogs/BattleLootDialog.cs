using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Gui.Text;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Dialogs;

/// <summary>
/// The post-combat loot window: lists the dead monsters' dropped items (+ gold / rations)
/// and offers Take All (distributes to the party via the standard give-to-party events)
/// or Leave. Modelled on the original's battle-loot list (MAIN.EXE fcn.0004e124).
/// </summary>
public class BattleLootDialog : ModalDialog
{
    readonly IReadOnlyList<(ItemId Item, ushort Amount)> _items;
    readonly int _gold;
    readonly int _rations;
    AlbionTaskCore<bool> _source;

    public BattleLootDialog(int depth, IReadOnlyList<(ItemId Item, ushort Amount)> items, int gold, int rations)
        : base(DialogPositioning.Center, depth)
    {
        _items = items ?? [];
        _gold = gold;
        _rations = rations;
        On<DismissMessageEvent>(_ => Close());
        On<UiRightClickEvent>(e => { e.Propagating = false; Close(); });
    }

    public AlbionTask Show()
    {
        _source = new AlbionTaskCore<bool>("BattleLootDialog.Show");
        IsActive = true;

        var tf = Resolve<ITextFormatter>();
        var rows = new List<IUiElement>();

        foreach (var (item, amount) in _items)
        {
            var data = Assets.LoadItem(item);
            var label = data == null
                ? new LiteralText(item.ToString())
                : (amount > 1
                    ? new LiteralText($"{Assets.LoadStringSafe(data.Name)} x{amount}")
                    : (IText)tf.NoWrap().Format(data.Name));
            rows.Add(new UiText(label));
        }

        if (_gold > 0)
            rows.Add(new UiText(new LiteralText($"{(decimal)_gold / 10:0.#} gold")));
        if (_rations > 0)
            rows.Add(new UiText(new LiteralText($"{_rations} rations")));

        var contents = new List<IUiElement>();
        if (rows.Count > 0)
        {
            contents.Add(new GroupingFrame(new VerticalStacker(rows)));
            contents.Add(new Spacing(0, 3));
        }

        contents.Add(new Button(new UiText(tf.Center().NoWrap().Format(TextId.From(Base.UAlbionString.TakeAll)))).OnClick(TakeAll));
        contents.Add(new Button(Base.SystemText.MsgBox_CloseWindow).OnClick(Close));

        AttachChild(new DialogFrame(new Padding(new VerticalStacker(contents), 3))
        {
            Background = DialogFrameBackgroundStyle.MainMenuPattern
        });
        return _source.Task.AsUntyped;
    }

    void TakeAll()
    {
        foreach (var (item, amount) in _items)
            Raise(new ModifyItemCountEvent(NumericOperation.AddAmount, (byte)Math.Min(byte.MaxValue, amount), item));
        if (_gold > 0)
            Raise(new ModifyGoldEvent(NumericOperation.AddAmount, (ushort)Math.Min(ushort.MaxValue, _gold), 0));
        if (_rations > 0)
            Raise(new ModifyRationsEvent(NumericOperation.AddAmount, (ushort)Math.Min(ushort.MaxValue, _rations), 0));
        Close();
    }

    void Close()
    {
        if (_source == null)
            return;
        IsActive = false;
        var source = _source;
        _source = null;
        source.SetResult(true);
    }
}
