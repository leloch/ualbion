namespace UAlbion.Game;

public enum BlockId
{
    MainText = -1, // Pseudo-block id for text filtering purposes in UiText
    Profession = 0,
    QueryWord = 1,
    QueryItem = 2,
    Farewell = 3,
    // Conversation-menu pseudo-options offered conditionally (only when the NPC's event set
    // has the matching action chain): join a recruitable NPC / dismiss a party member.
    AskToJoin = 4,
    AskToLeave = 5
    // Regular dialogue-line blocks start at 10
}