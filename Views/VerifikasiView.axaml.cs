using Avalonia.Controls;
using AvaloniaFingerprint.ViewModels;

namespace AvaloniaFingerprint.Views;

public partial class VerifikasiView : UserControl
{
    public VerifikasiView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => AttachTopLevel();
        DataContextChanged += (_, _) => AttachTopLevel();
    }

    private void AttachTopLevel()
    {
        if (DataContext is VerifikasiViewModel viewModel)
            viewModel.TopLevel = TopLevel.GetTopLevel(this);
    }
}
