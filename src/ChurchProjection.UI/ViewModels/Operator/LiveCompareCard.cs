using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>One alternate-translation card in Now Live. Double-click sends it to Program.</summary>
public sealed class LiveCompareCard : ReactiveObject
{
    private string _title = "";
    private string _body = "Loading…";
    private string _footer = "";
    private bool _isReady;

    public string Translation { get; init; } = "";

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string Body
    {
        get => _body;
        set => this.RaiseAndSetIfChanged(ref _body, value);
    }

    public string Footer
    {
        get => _footer;
        set => this.RaiseAndSetIfChanged(ref _footer, value);
    }

    public bool IsReady
    {
        get => _isReady;
        set => this.RaiseAndSetIfChanged(ref _isReady, value);
    }
}
