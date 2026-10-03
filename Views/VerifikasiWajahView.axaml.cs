using Avalonia.Controls;
using AvaloniaFingerprint.ViewModels;

namespace AvaloniaFingerprint.Views;

public partial class VerifikasiWajahView : UserControl
{
    public VerifikasiWajahView()
    {
        InitializeComponent();

        // ViewModel tidak punya akses ke jendela, sedangkan dialog pilih file dimiliki oleh
        // StorageProvider milik TopLevel. Referensinya diserahkan dari sini, dan dipasang pada dua
        // event karena urutan "terpasang ke visual tree" dan "DataContext terisi" tidak dijamin.
        AttachedToVisualTree += (_, _) => AttachTopLevel();
        DataContextChanged += (_, _) => AttachTopLevel();
    }

    private void AttachTopLevel()
    {
        if (DataContext is VerifikasiWajahViewModel viewModel)
            viewModel.TopLevel = TopLevel.GetTopLevel(this);
    }
}
