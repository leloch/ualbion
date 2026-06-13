using System.Collections.Generic;

namespace UAlbion.Game.Text;

public class TextSourceWrapper : IText
{
    IText _source;
    int _version = 1;
    int _baseSourceVersion;

    public IText Source
    {
        get => _source;
        set
        {
            _version++;
            _source = value;
            _baseSourceVersion = _source?.Version ?? 0;
        }
    }

    // TXT-05: ?? binds looser than -, so the old form parsed as _version + (Version ?? (0 - base))
    // and never subtracted _baseSourceVersion for a non-null source. Parenthesise the coalesce.
    public int Version => _version + (_source?.Version ?? 0) - _baseSourceVersion;
    public IEnumerable<TextBlock> GetBlocks() =>
        _source == null
            ? []
            : _source.GetBlocks();
}
