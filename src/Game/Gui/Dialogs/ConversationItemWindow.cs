using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Gui.Text;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Dialogs;

/// <summary>
/// The "ask about / show an item" picker used by conversations (BlockId.QueryItem):
/// lists the talking party member's inventory items and resolves to the chosen ItemId
/// (or ItemId.None when dismissed). Mirrors ConversationTopicWindow's awaitable pattern.
/// </summary>
public class ConversationItemWindow : ModalDialog
{
    AlbionTaskCore<ItemId> _source;

    public ConversationItemWindow(int depth) : base(DialogPositioning.Center, depth)
    {
        On<UiRightClickEvent>(e =>
        {
            e.Propagating = false;
            OnItemSelected(ItemId.None);
        });

        On<DismissMessageEvent>(_ => OnItemSelected(ItemId.None));
    }

    void OnItemSelected(ItemId id)
    {
        if (_source == null)
            return;

        IsActive = false;
        var source = _source;
        _source = null;
        source.SetResult(id);
    }

    public AlbionTask<ItemId> GetItem(IReadOnlyList<(ItemId Id, IText Name)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_source != null)
            throw new InvalidOperationException("Tried to get an item while another request was in progress");

        RemoveAllChildren();
        IsActive = true;
        _source = new AlbionTaskCore<ItemId>("ConversationItemWindow.GetItem");

        var elements = new List<IUiElement>();
        var buttons = new List<IUiElement>();
        foreach (var (id, name) in items)
        {
            var captured = id;
            buttons.Add(new Button(new UiText(name))
            {
                Theme = ButtonTheme.Frameless
            }.OnClick(() => OnItemSelected(captured)));
        }

        if (buttons.Count > 0)
        {
            elements.Add(new GroupingFrame(new VerticalStacker(buttons)));
            elements.Add(new Spacing(0, 3));
        }

        elements.Add(new Button(Base.SystemText.MsgBox_CloseWindow).OnClick(() => OnItemSelected(ItemId.None)));

        AttachChild(new DialogFrame(new Padding(new VerticalStacker(elements), 3)) { Background = DialogFrameBackgroundStyle.MainMenuPattern });
        return _source.Task;
    }
}
