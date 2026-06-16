namespace ChurchProjection.Core.Models.Slides;

/// <summary>
/// An ordered set of slides produced from a single piece of content (e.g. a multi-verse
/// passage broken into verse-by-verse pages, or a song broken into stanzas). The operator
/// pages through it with the arrow keys.
/// </summary>
public sealed class SlideDeck
{
    private readonly List<Slide> _slides;

    public SlideDeck(IEnumerable<Slide> slides, int startIndex = 0)
    {
        _slides = slides.ToList();
        if (_slides.Count == 0)
            _slides.Add(Slide.Blank());
        CurrentIndex = Math.Clamp(startIndex, 0, _slides.Count - 1);
    }

    public IReadOnlyList<Slide> Slides => _slides;
    public int CurrentIndex { get; private set; }
    public int Count => _slides.Count;
    public Slide Current => _slides[CurrentIndex];
    public bool IsMulti => _slides.Count > 1;

    public static SlideDeck Single(Slide slide) => new([slide]);

    /// <summary>Advances to the next slide. Returns false if already on the last slide.</summary>
    public bool MoveNext()
    {
        if (CurrentIndex >= _slides.Count - 1)
            return false;
        CurrentIndex++;
        return true;
    }

    /// <summary>Steps back to the previous slide. Returns false if already on the first slide.</summary>
    public bool MovePrev()
    {
        if (CurrentIndex <= 0)
            return false;
        CurrentIndex--;
        return true;
    }
}

/// <summary>The operator-facing position within the live deck (zero-based index out of count).</summary>
public readonly record struct DeckPosition(int Index, int Count)
{
    public bool IsMulti => Count > 1;
    public string Label => $"{Index + 1} / {Count}";
}
