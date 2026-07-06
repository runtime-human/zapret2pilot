using System;
using Avalonia.Controls;
using ReactiveUI;

namespace Zapret2Pilot.App.Shell;

public sealed partial class MainWindow : Window, IViewFor<MainWindowViewModel>
{
    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML compiler.
    /// This constructor exists solely so that the generated
    /// <c>InitializeComponent</c> call has a public, parameterless
    /// target. It is not intended for production use: in production the
    /// shell composes <see cref="MainWindow"/> through the Generic Host
    /// composition root with an explicit <see cref="MainWindowViewModel"/>
    /// and assigns it to <c>desktop.MainWindow</c> inside
    /// <c>Program.Main</c>. Any direct invocation from XAML, from the
    /// previewer, or from a stray caller is a programming error and
    /// throws an <see cref="InvalidOperationException"/> so the failure
    /// is loud and immediate.
    /// </summary>
    public MainWindow()
        : this(ThrowIfNoViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        // ReactiveUI activation: the view and its view model
        // (IActivatableViewModel) share an activation window managed by
        // ReactiveUI. Subscriptions created inside WhenActivated are
        // disposed when the window deactivates, so no ReactiveUI
        // observer outlives the shell. The current implementation has
        // no view-side subscriptions; the block is kept as the single
        // hook for any future view-only wiring (e.g. focus / lifecycle
        // events) and to document the activation contract.
        this.WhenActivated(disposables => { });
    }

    public MainWindowViewModel? ViewModel { get; set; }

    object? IViewFor.ViewModel
    {
        get => ViewModel;
        set => ViewModel = (MainWindowViewModel?)value;
    }

    private static MainWindowViewModel ThrowIfNoViewModel()
    {
        throw new InvalidOperationException(
            "MainWindow must be constructed through the Generic Host composition root with a MainWindowViewModel. "
            + "The parameterless constructor exists only for the Avalonia XAML compiler and must not be called from production code.");
    }
}
