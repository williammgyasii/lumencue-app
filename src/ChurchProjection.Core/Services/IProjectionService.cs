using ChurchProjection.Core.Models.Slides;

namespace ChurchProjection.Core.Services;

public interface IProjectionService
{
    IObservable<Slide> CurrentSlide { get; }

    /// <summary>Position within the live deck; emits on every projection and page change.</summary>
    IObservable<DeckPosition> Position { get; }

    Slide Current { get; }

    /// <summary>Projects a single slide (wrapped as a one-page deck).</summary>
    void ProjectSlide(Slide slide);

    /// <summary>Projects a multi-page deck, showing its current page.</summary>
    void ProjectDeck(SlideDeck deck);

    /// <summary>Pages forward within the live deck. Returns false if already on the last page.</summary>
    bool MoveNext();

    /// <summary>Pages back within the live deck. Returns false if already on the first page.</summary>
    bool MovePrev();

    void GoBlank();
}
