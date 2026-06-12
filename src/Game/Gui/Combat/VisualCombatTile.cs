using System;
using System.Numerics;
using UAlbion.Core;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Gui.Text;
using UAlbion.Game.State;

namespace UAlbion.Game.Gui.Combat;

public class VisualCombatTile : UiElement
{
    const int Width = 32;
    const int Height = 24;
    // const int SpriteHeight = 48;

    readonly int _tileIndex;
    readonly IReadOnlyBattle _battle;
    readonly UiSpriteElement _sprite;
    readonly Button _button;
    readonly SimpleText _feedback;
    readonly SimpleText _hpText;

    public SpriteId Icon
    {
        get => _sprite.Id;
        set
        {
            if (_sprite.Id == value) return;
            _sprite.Id = value;
            _sprite.SubId = 0; // frame index is per-texture; reset when the occupant changes
            _sprite.IsActive = !value.IsNone;
        }
    }

    public VisualCombatTile(int tileIndex, IReadOnlyBattle battle)
    {
        On<PostEngineUpdateEvent>(_ => OnPostUpdate());
        On<CombatTurnHighlightEvent>(e =>
        {
            // New active combatant: highlight them, and clear the hit feedback the
            // previous turn left on any tile.
            _sprite.Flags = e.TileIndex == _tileIndex
                ? _sprite.Flags | SpriteFlags.Highlight
                : _sprite.Flags & ~SpriteFlags.Highlight;
            ClearHitFeedback();
        });
        On<CombatHitEvent>(e =>
        {
            if (e.TileIndex != _tileIndex)
                return;
            _sprite.Flags &= ~(SpriteFlags.RedTint | SpriteFlags.GreenTint);
            if (e.Amount > 0)
                _sprite.Flags |= e.Heal ? SpriteFlags.GreenTint : SpriteFlags.RedTint;
            _feedback.Text = e.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
        _tileIndex = tileIndex;
        _battle = battle ?? throw new ArgumentNullException(nameof(battle));
        _sprite = new UiSpriteElement(SpriteId.None) { IsActive = false, Flags = SpriteFlags.BottomAligned };
        _feedback = new SimpleText(string.Empty);
        _hpText = new SimpleText(string.Empty);

        var stack =
            new VerticalStacker(
                    new Spacing(Width, Height),
                    _sprite
            )
            {
                Greedy = false
            };

        // Damage/heal numbers overlay the tile contents during round playback;
        // the View of Life LP readout stacks above them.
        var layers = new LayerStacker(stack, _feedback, _hpText);

        _button = new Button(layers) { Margin = 0 }
            .OnHover(() => Hover?.Invoke())
            .OnBlur(() => Blur?.Invoke())
            .OnClick(() => Click?.Invoke())
            .OnRightClick(() => RightClick?.Invoke())
            .OnDoubleClick(() => DoubleClick?.Invoke())
            .OnButtonDown(() => ButtonDown?.Invoke());
        AttachChild(_button);
    }

    // Idle animation: cycle the tactical sprite's frames like the original's combat grid.
    // PostEngineUpdate fires per render frame; stepping every 15 gives ~4 fps at 60 Hz.
    const int FramesPerAnimStep = 15;
    int _frameCounter;

    void OnPostUpdate()
    {
        ICombatParticipant mob = _battle.GetTile(_tileIndex);
        Icon = mob == null ? SpriteId.None : mob.Effective.TacticalGfx;

        // View of Life: show monster LP on the grid while the spell is active.
        _hpText.Text =
            CombatBuffs.ViewOfLife
            && mob != null
            && mob.SheetId.Type == UAlbion.Config.AssetType.MonsterSheet
                ? _battle.GetLifePoints(mob).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;

        if (_sprite.Id.IsNone || ++_frameCounter % FramesPerAnimStep != 0)
            return;

        var texture = Assets.LoadTexture(_sprite.Id);
        int frames = texture?.Regions?.Count ?? 1;
        if (frames > 1)
            _sprite.SubId = (_sprite.SubId + 1) % frames;
    }

    void ClearHitFeedback()
    {
        _sprite.Flags &= ~(SpriteFlags.RedTint | SpriteFlags.GreenTint);
        _feedback.Text = string.Empty;
    }

    // public ButtonState State { get => _frame.State; set => _frame.State = value; }
    public override Vector2 GetSize() => new(Width, Height);
    public VisualCombatTile OnClick(Action callback) { Click += callback; return this; }
    public VisualCombatTile OnRightClick(Action callback) { RightClick += callback; return this; }
    public VisualCombatTile OnDoubleClick(Action callback) { DoubleClick += callback; return this; }
    public VisualCombatTile OnButtonDown(Action callback) { ButtonDown += callback; return this; }
    public VisualCombatTile OnHover(Action callback) { Hover += callback; return this; }
    public VisualCombatTile OnBlur(Action callback) { Blur += callback; return this; }

    event Action Click;
    event Action DoubleClick;
    event Action RightClick;
    event Action ButtonDown;
    event Action Hover;
    event Action Blur;

    public bool Hoverable { get => _button.Hoverable; set => _button.Hoverable = value; }
    public bool SuppressNextDoubleClick { get => _button.SuppressNextDoubleClick; set => _button.SuppressNextDoubleClick = value; }

    // ReSharper disable once UnusedParameter.Local
    static void Rebuild(in Rectangle extents)
    {
    }

    public override int Render(Rectangle extents, int order, LayoutNode parent)
    {
        Rebuild(extents);
        return base.Render(extents, order, parent);
    }
}