using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
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
        string url = ApiUrlTextBox.Text;
        if (string.IsNullOrWhiteSpace(url)) return;

        using (var client = new HttpClient()) {
          var response = await client.GetStringAsync($"{url.TrimEnd('/')}/api/tags");
          using (var doc = JsonDocument.Parse(response)) {
            var modelNames = new List<string>();
            foreach (var model in doc.RootElement.GetProperty("models").EnumerateArray()) {
              modelNames.Add(model.GetProperty("name").GetString());
            }

            ModelComboBox.ItemsSource = modelNames;
          }
        }
      } catch {
        StatusText.Text = "Failed to load model list.";
      }
    }
  }
}