using System;
using System.Text.Json.Serialization;
using SerdesNet;
using UAlbion.Api;
using UAlbion.Config;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Ids;

namespace UAlbion.Formats.Assets.Save;

public class NpcState : IMovementState
{
    // Total size = 128 bytes
    public static NpcState Serdes(SerdesName i, NpcState npc, (MapType mapType, AssetMapping mapping) c, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        npc ??= new NpcState();
        var startOffset = s.Offset;

        s.Begin("Npc" + i);
        // Mirror of the read-side +980 offset below (party-companion NPCs store the index).
        ushort id = npc.Id.Type == AssetType.EventSet && npc.Type == NpcType.Party && npc.Id.Id >= MapNpc.PartyNpcEventSetOffset
            ? (ushort)(npc.Id.Id - MapNpc.PartyNpcEventSetOffset)
            : (byte)npc.Id.ToDisk(c.mapping);
        id = s.UInt16(nameof(Id), id); // 0

        // The on-disk field is a raw u16 gfx index; the TYPE (ObjectGroup for 3D, Npc{Large,Small}Gfx
        // for 2D) is implied by the map. On WRITE we must serialize each NPC by ITS OWN current type —
        // not the passed mapType — because during a map transition / map-entry autosave the live NPC
        // list can momentarily hold 3D ObjectGroup NPCs while the current map already reports TwoD (or
        // vice versa). The old code keyed solely on mapType, so that mismatch fed an ObjectGroup id into
        // SpriteId.SerdesU16 → "Tried to construct a SpriteId with a type of ObjectGroup" → the whole
        // save (incl. the autosave) threw. On READ there is no existing value, so we use the map's type;
        // the loader re-keys NPC gfx to the live map afterwards regardless.
        AssetType serdesType;
        if (s.IsWriting())
        {
            serdesType = npc.SpriteOrGroup.Type switch
            {
                AssetType.ObjectGroup => AssetType.ObjectGroup,
                AssetType.NpcSmallGfx => AssetType.NpcSmallGfx,
                _ => AssetType.NpcLargeGfx // None / NpcLargeGfx / anything else → Sprite path (writes the raw index)
            };
        }
        else
        {
            serdesType = c.mapType switch
            {
                MapType.ThreeD => AssetType.ObjectGroup,
                MapType.TwoD => AssetType.NpcLargeGfx,
                MapType.TwoDOutdoors => AssetType.NpcSmallGfx,
                _ => throw new ArgumentOutOfRangeException(nameof(c), c.mapType, null)
            };
        }

        npc.SpriteOrGroup = serdesType == AssetType.ObjectGroup
            ? AssetId.SerdesU16(nameof(SpriteOrGroup), npc.SpriteOrGroup, AssetType.ObjectGroup, c.mapping, s)
            : SpriteId.SerdesU16(nameof(SpriteOrGroup), npc.SpriteOrGroup, serdesType, c.mapping, s);

        npc.Type = s.EnumU8(nameof(Type), npc.Type);
        npc.NoClip = s.UInt16(nameof(NoClip), (ushort)(npc.NoClip ? 1 : 0)) != 0;
        npc.Sound = s.UInt16(nameof(Sound), npc.Sound);
        npc.ActiveSfx0 = s.UInt16(nameof(ActiveSfx0), npc.ActiveSfx0);
        npc.ActiveSfx1 = s.UInt16(nameof(ActiveSfx1), npc.ActiveSfx1);
        npc.ActiveSfx2 = s.UInt16(nameof(ActiveSfx2), npc.ActiveSfx2);
        npc.ActiveSfx3 = s.UInt16(nameof(ActiveSfx3), npc.ActiveSfx3);
        npc.Triggers = s.EnumU16(nameof(Triggers), npc.Triggers);
        npc.EventIndex = s.UInt16(nameof(EventIndex), npc.EventIndex);
        npc.MovementType = s.EnumU8(nameof(MovementType), npc.MovementType);
        s.Pad(1);
        npc.WasActive = s.UInt16(nameof(WasActive), npc.WasActive);
        npc.Flags = s.EnumU8(nameof(Flags), npc.Flags);
        npc.Unk1A = s.UInt8(nameof(Unk1A), npc.Unk1A);
        npc.Unk1B = s.UInt16(nameof(Unk1B), npc.Unk1B);
        npc.Unk1D = s.UInt16(nameof(Unk1D), npc.Unk1D); // [0..11]
        npc.WaypointDataOffset = s.UInt32(nameof(WaypointDataOffset), npc.WaypointDataOffset);
        npc.Unk23 = s.UInt16(nameof(Unk23), npc.Unk23);
        npc.Angle = s.UInt16(nameof(Angle), npc.Angle);
        npc.WaypointIndex = s.UInt16(nameof(WaypointIndex), npc.WaypointIndex);
        npc.Unk29 = s.UInt8(nameof(Unk29), npc.Unk29); // State machine var? [0..5]
        npc.X = s.UInt16(nameof(X), npc.X); // 2A Current tile position
        npc.Y = s.UInt16(nameof(Y), npc.Y); // 2C
        npc.X2 = s.UInt16(nameof(X2), npc.X2); // 2E
        npc.Y2 = s.UInt16(nameof(Y2), npc.Y2); // 30
        npc.PixelX = s.Int32(nameof(PixelX), (int)npc.PixelX); // 32
        npc.PixelY = s.Int32(nameof(PixelY), (int)npc.PixelY); // 36
        npc.PixelDeltaX = s.Int32(nameof(PixelDeltaX), npc.PixelDeltaX); // 3A
        npc.PixelDeltaY = s.Int32(nameof(PixelDeltaY), npc.PixelDeltaY); // 3E
        npc.Unk42 = s.UInt16(nameof(Unk42), npc.Unk42); // 42
        npc.OldX = s.UInt16(nameof(OldX), npc.OldX); // Old? 44
        npc.OldY = s.UInt16(nameof(OldY), npc.OldY); // 46
        npc.MoveToX = s.UInt16(nameof(MoveToX), npc.MoveToX); // Target? 48
        npc.MoveToY = s.UInt16(nameof(MoveToY), npc.MoveToY); // 4A
        npc.Unk4C = s.UInt16(nameof(Unk4C), npc.Unk4C); // 4C
        npc.Unk4E = s.UInt16(nameof(Unk4E), npc.Unk4E);
        npc.Unk50 = s.UInt8(nameof(Unk50), npc.Unk50);
        npc.Unk51 = s.UInt8(nameof(Unk51), npc.Unk51);
        npc.Unk52 = s.UInt8(nameof(Unk52), npc.Unk52);
        npc.Unk53 = s.UInt8(nameof(Unk53), npc.Unk53);
        npc.Unk54 = s.UInt16(nameof(Unk54), npc.Unk54);
        npc.Unk56 = s.UInt16(nameof(Unk56), npc.Unk56);
        npc.Unk58 = s.UInt16(nameof(Unk58), npc.Unk58);
        npc.GfxWidth = s.UInt16(nameof(GfxWidth), npc.GfxWidth);
        npc.GfxHeight = s.UInt16(nameof(GfxHeight), npc.GfxHeight);
        npc.Unk5EGfxRelated = s.UInt16(nameof(Unk5EGfxRelated), npc.Unk5EGfxRelated);
        npc.GfxAlloc = s.UInt32(nameof(GfxAlloc), npc.GfxAlloc);
        npc.Unk64 = s.UInt8(nameof(Unk64), npc.Unk64);
        npc.Unk65 = s.UInt8(nameof(Unk65), npc.Unk65);
        npc.Unk66 = s.UInt16(nameof(Unk66), npc.Unk66);
        NpcMoveState.Serdes(npc.NpcMoveState, s);

        // Party-companion NPCs store the party-member index; their on-talk EventSet is
        // 980 + index (see MapNpc.PartyNpcEventSetOffset). Without this the id resolved to
        // the wrong low EventSet and recruit-by-talking was inert.
        var assetType = MapNpc.AssetTypeForNpcType(npc.Type, (npc.Flags & NpcFlags.SimpleMsg) != 0);
        // id 0 = empty slot (party index 0 is Tom, never a map NPC) — leave unoffset.
        int resolvedId = assetType == AssetType.EventSet && id != 0 ? id + MapNpc.PartyNpcEventSetOffset : id;
        npc.Id = AssetId.FromDisk(assetType, resolvedId, c.mapping);

        ApiUtil.Assert(s.Offset == startOffset + 0x80);
        s.End();
        return npc;
    }

    [JsonIgnore] public IEventSet EventSet { get; set; }
    public AssetId Id { get; set; } // 0
    public AssetId SpriteOrGroup { get; set; } // 2
    public NpcType Type { get; set; } // 4
    public bool NoClip { get; set; } // 5
    public ushort Sound { get; set; } // 8. Always 0?
    public ushort ActiveSfx0 { get; set; } // 9
    public ushort ActiveSfx1 { get; set; } // B
    public ushort ActiveSfx2 { get; set; } // D
    public ushort ActiveSfx3 { get; set; } // F
    public TriggerTypes Triggers { get; set; } // 11
    public ushort EventIndex { get; set; } // 13 Always 0xffff?
    public NpcMovement MovementType { get; set; }
    public ushort WasActive { get; set; }
    public NpcFlags Flags { get; set; }
    public byte Unk1A { get; set; }
    public ushort Unk1B { get; set; }
    public ushort Unk1D { get; set; }
    public uint WaypointDataOffset { get; set; }
    public ushort Unk23 { get; set; }
    public ushort Angle { get; set; }
    public ushort WaypointIndex { get; set; }
    public byte Unk29 { get; set; }
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public ushort X2 { get; set; }
    public ushort Y2 { get; set; }

    public float PixelX { get; set; }
    public float PixelY { get; set; }
    public int PixelDeltaX { get; set; }
    public int PixelDeltaY { get; set; }
    public ushort Unk42 { get; set; }
    public ushort OldX { get; set; }
    public ushort OldY { get; set; }
    public ushort MoveToX { get; set; }
    public ushort MoveToY { get; set; }
    public ushort Unk4C { get; set; }
    public ushort Unk4E { get; set; }
    public byte Unk50 { get; set; }
    public byte Unk51 { get; set; }
    public byte Unk52 { get; set; }
    public byte Unk53 { get; set; }
    public ushort Unk54 { get; set; }
    public ushort Unk56 { get; set; } // Probably flags
    public ushort Unk58 { get; set; }
    public ushort GfxWidth { get; set; }
    public ushort GfxHeight { get; set; }
    public ushort Unk5EGfxRelated { get; set; }
    public uint GfxAlloc { get; set; }
    public byte Unk64 { get; set; }
    public byte Unk65 { get; set; }
    public ushort Unk66 { get; set; }
    public NpcMoveState NpcMoveState { get; } = new();

    public int StartTick { get; set; }
    public int MovementTick { get; set; }
    public bool HasTarget { get; set; }
    public Direction FacingDirection
    {
        get => NpcMoveState.Direction;
        set => NpcMoveState.Direction = value;
    }
}
