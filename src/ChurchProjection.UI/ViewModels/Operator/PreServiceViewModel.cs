using System;
using System.Reactive;
using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// Backs the "Pre-service" dialog: launch a self-ticking countdown (e.g. "Service begins in 5:00")
/// or a live wall clock onto the active output. The work is delegated back to the operator so the
/// slides flow through the normal projection pipeline.
/// </summary>
public sealed class PreServiceViewModel : ReactiveObject
{
    private readonly OperatorViewModel _operator;
    private double _minutes = 5;
    private string _heading = "Service begins in";
    private string _doneMessage = "Welcome!";
    private string _clockHeading = "";

    public PreServiceViewModel(OperatorViewModel op)
    {
        _operator = op;

        StartCountdownCommand = ReactiveCommand.Create(() =>
        {
            _operator.StartCountdown(Minutes, Heading, DoneMessage);
            CloseRequested?.Invoke();
        });

        ShowClockCommand = ReactiveCommand.Create(() =>
        {
            _operator.ShowClock(ClockHeading);
            CloseRequested?.Invoke();
        });

        ClearCommand = ReactiveCommand.Create(() =>
        {
            _operator.BlankCommand.Execute(Unit.Default).Subscribe();
            CloseRequested?.Invoke();
        });

        SetMinutesCommand = ReactiveCommand.Create<string>(m =>
        {
            if (double.TryParse(m, out var v)) Minutes = v;
        });
    }

    public double Minutes { get => _minutes; set => this.RaiseAndSetIfChanged(ref _minutes, Math.Clamp(value, 0, 600)); }
    public string Heading { get => _heading; set => this.RaiseAndSetIfChanged(ref _heading, value); }
    public string DoneMessage { get => _doneMessage; set => this.RaiseAndSetIfChanged(ref _doneMessage, value); }
    public string ClockHeading { get => _clockHeading; set => this.RaiseAndSetIfChanged(ref _clockHeading, value); }

    public ReactiveCommand<Unit, Unit> StartCountdownCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowClockCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }
    public ReactiveCommand<string, Unit> SetMinutesCommand { get; }

    public event Action? CloseRequested;
}
