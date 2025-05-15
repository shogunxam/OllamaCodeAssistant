using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace OllamaCodeAssistant.Options {
  [Guid("d89fe89d-9bfa-4e1e-a1fc-b3c38876aaaf")]
  public class OllamaOptionsPage : UIElementDialogPage {
    private OllamaOptionsControl control;

    public string OllamaApiUrl { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "llama3";

    protected override UIElement Child {
      get {
        control = new OllamaOptionsControl();
        control.ApiUrlTextBox.Text = OllamaApiUrl;
        control.ModelComboBox.ItemsSource = new[] { DefaultModel }; // Placeholder
        control.ModelComboBox.Text = DefaultModel;
        return control;
      }
    }

    protected override void OnApply(PageApplyEventArgs e) {
      OllamaApiUrl = control.ApiUrlTextBox.Text;
      DefaultModel = control.ModelComboBox.Text;
      base.OnApply(e);
    }
  }
}
