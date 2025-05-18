using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OllamaCodeAssistant.Options {

  public partial class OllamaOptionsControl : UserControl {

    public OllamaOptionsControl() {
      InitializeComponent();
      Loaded += ControlLoaded;
    }

    private async void ControlLoaded(object sender, RoutedEventArgs e) {
      await RefreshModelList();
    }

    private async void RefreshModelListButtonClicked(object sender, RoutedEventArgs e) {
      await RefreshModelList();
    }

    private async Task RefreshModelList() {
      try {
        ModelComboBox.ItemsSource = null;
        ModelComboBox.ItemsSource = await OllamaManager.GetAvailableModelsAsync(ApiUrlTextBox.Text);
      } catch {
        StatusText.Text = "Failed to load model list.";
      }
    }
  }
}