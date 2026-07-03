using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAlbion.Api;
using UAlbion.Config;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Ids;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Gui.Dialogs;
using UAlbion.Game.Gui.Text;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Menus;

public class PickSaveSlotMenu : ModalDialog
{
    readonly bool _showEmptySlots;
    readonly StringId _stringId;
    const ushort MaxSaveNumber = 99;
    const int SlotsPerPage = 10;
    int _page;

    public PickSaveSlotMenu(bool showEmptySlots, TextId textId, int depth) : this(showEmptySlots, new StringId(textId), depth) { }
    public PickSaveSlotMenu(bool showEmptySlots, StringId stringId, int depth) : base(DialogPositioning.Center, depth)
    {
        On<UiRightClickEvent>(e =>
        {
            Remove();
            Closed?.Invoke(this, null);
            e.Propagating = false;
        });
        On<CloseWindowEvent>(e =>
        {
            Remove();
            Closed?.Invoke(this, null);
            e.Propagating = false;
        });

        _showEmptySlots = showEmptySlots;
        _stringId = stringId;
    }

    public event EventHandler<ushort?> Closed;

    void PickSlot(ushort slotNumber)
    {
        Closed?.Invoke(this, slotNumber);
        Remove();
    }

    string BuildSaveFilename(ushort i)
    {
        var pathResolver = Resolve<IPathResolver>();
        // TODO: This path currently exists in two places: here and Game\State\GameState.cs
        return pathResolver.ResolvePath($"$(SAVES)/SAVE.{i:D3}");
    }

    const int MaxSaveSlotWidth = 280;
    protected override void Subscribed() => Rebuild();

    // Pages beyond the first only appear when needed: the save dialog always offers all
    // 99 slots; the load dialog pages up to the highest slot that actually has a file.
    int PageCount(IFileSystem disk)
    {
        if (_showEmptySlots)
            return (MaxSaveNumber + SlotsPerPage - 1) / SlotsPerPage;

        int highest = 0;
        for (ushort i = 1; i <= MaxSaveNumber; i++)
            if (disk.FileExists(BuildSaveFilename(i)))
                highest = i;
        return Math.Max(1, (highest + SlotsPerPage - 1) / SlotsPerPage);
    }

    void Rebuild()
    {
        RemoveAllChildren();
        var disk = Resolve<IFileSystem>();
        int pageCount = PageCount(disk);
        if (_page >= pageCount) _page = pageCount - 1;
        if (_page < 0) _page = 0;

        IText BuildEmptySlotText(int x) =>
            new DynamicText(() =>
            {
                var textFormatter = Resolve<ITextFormatter>();
                var block = textFormatter
                    .Ink(Base.Ink.Gray)
                    .Format(Base.SystemText.MainMenu_EmptyPosition)
                    .GetBlocks().Single();
                block.Text = $"{x,2}    {block.Text}";
                return [block];
            });

        var buttons = new List<IUiElement>();
        var first = (ushort)(_page * SlotsPerPage + 1);
        var last = (ushort)Math.Min(first + SlotsPerPage - 1, MaxSaveNumber);
        for (ushort i = first; i <= last; i++)
        {
            var filename = BuildSaveFilename(i);
            if (disk.FileExists(filename))
            {
                using var s = AlbionSerdes.CreateReader(disk.OpenRead(filename));
                var name = SavedGame.GetName(s) ?? "Invalid";
                var text = $"{i,2}    {name}";
                ushort slotNumber = i;
                buttons.Add(new ConversationOption(new LiteralText(text), MaxSaveSlotWidth, null, () => PickSlot(slotNumber)));
            }
            else if (_showEmptySlots)
            {
                var text = BuildEmptySlotText(i);
                ushort slotNumber = i;
                buttons.Add(new ConversationOption(text, MaxSaveSlotWidth, null, () => PickSlot(slotNumber)));
            }
        }

        var elements = new List<IUiElement> { new Spacing(MaxSaveSlotWidth, 0) };
        elements.AddRange(buttons);
        elements.Add(new Spacing(0, 4));

        if (pageCount > 1)
        {
            elements.Add(new HorizontalStacker(
                new Button("<").OnClick(() => { if (_page > 0) { _page--; Rebuild(); } }),
                new Spacing(4, 0),
                new SimpleText($"{_page + 1} / {pageCount}").Center(),
                new Spacing(4, 0),
                new Button(">").OnClick(() => { if (_page < pageCount - 1) { _page++; Rebuild(); } })));
            elements.Add(new Spacing(0, 4));
        }

        var header = new UiTextBuilder(_stringId).Center().NoWrap();
        elements.Add(new ButtonFrame(new Padding(header, 2)) { State = ButtonState.Pressed });

        var stack = new VerticalStacker(elements);
        AttachChild(new DialogFrame(new Padding(stack, 4, 5, 6, 5)));
    }
}
