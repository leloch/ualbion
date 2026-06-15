using System;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Gui.Text;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Dialogs;

public class ConversationOption : UiElement
{
    readonly Action _action;

    // Exposed so test tooling (the HTTP harness /conversation dump) can read the live option
    // text + block without walking the private child UI tree.
    public IText Text { get; }
    public BlockId? BlockId { get; }

    public ConversationOption(IText text, int maxWidth, BlockId? blockId, Action action)
    {
        _action = action;
        Text = text;
        BlockId = blockId;
        AttachChild(
            new Button(new UiText(text, maxWidth) { BlockFilter = blockId })
            {
                // Full width, invisible except hover (then white background w/ alpha blend)
                Theme = ButtonTheme.Frameless
            }.OnClick(action));
    }

    public void Trigger() => _action();
}