using System.Reactive.Linq;
using System.Reactive.Subjects;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

public class ProjectionService : IProjectionService
{
    private readonly BehaviorSubject<Slide> _currentSlide = new(Slide.Blank());
    private readonly BehaviorSubject<DeckPosition> _position = new(new DeckPosition(0, 1));
    private SlideDeck _deck = SlideDeck.Single(Slide.Blank());

    public IObservable<Slide> CurrentSlide => _currentSlide.AsObservable();
    public IObservable<DeckPosition> Position => _position.AsObservable();
    public Slide Current => _currentSlide.Value;

    public void ProjectSlide(Slide slide) => ProjectDeck(SlideDeck.Single(slide));

    public void ProjectDeck(SlideDeck deck)
    {
        _deck = deck;
        Publish();
    }

    public bool MoveNext()
    {
        if (!_deck.MoveNext())
            return false;
        Publish();
        return true;
    }

    public bool MovePrev()
    {
        if (!_deck.MovePrev())
            return false;
        Publish();
        return true;
    }

    public void GoBlank() => ProjectDeck(SlideDeck.Single(Slide.Blank()));

    private void Publish()
    {
        _currentSlide.OnNext(_deck.Current);
        _position.OnNext(new DeckPosition(_deck.CurrentIndex, _deck.Count));
    }
}
