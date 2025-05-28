using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OllamaCodeAssistant.Options;

namespace OllamaCodeAssistant {

  public partial class ChatToolWindowControl : UserControl {
    private readonly ChatToolWindow _chatToolWindow;
    private LLMInteractionManager _llmInteractionManager;

    public ChatToolWindowControl(ChatToolWindow chatToolWindow) {
      _chatToolWindow = chatToolWindow;

      OllamaAsyncQuickInfoSource.ChatToolWindowControl = this;

      InitializeComponent();
      ClearError();
    }

    #region Event Handlers

#pragma warning disable VSTHRD100 // Avoid async void methods

    private async Task InitializeControlAsync() {
      Loaded -= ControlLoaded; // Unsubscribe from the event to prevent multiple calls

      var package = _chatToolWindow?.Package as OllamaCodeAssistantPackage;
      _llmInteractionManager = package?.GetLLMInteractionManager() ?? throw new ApplicationException("Unable to load LLM Interaction Manager");

      _llmInteractionManager.OnResponseReceived += AppendMessageToUI;
      _llmInteractionManager.OnErrorOccurred += DisplayError;
      _llmInteractionManager.OnLogEntryReceived += AppendMessageToLog;

      await PopulateModelSelectionComboBox(_llmInteractionManager.Options);

      try {
        await InitializeWebViewAsync(MarkdownWebView);
      } catch (Exception ex) {
        DisplayError($"Error loading WebView2: {ex.Message}");
      }

      TextSelectionListener.SelectionChanged += TextViewTrackerSelectionChanged;

      UserInputTextBox.Focus();
    }

    private async void SubmitButtonClicked(object sender, RoutedEventArgs e) => await HandleSubmitButtonClickAsync();

    private async void ControlLoaded(object sender, RoutedEventArgs e) => await InitializeControlAsync();

    private async void TextViewTrackerSelectionChanged(object sender, string e) {
      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
      ContextIncludeSelection.IsChecked = !string.IsNullOrEmpty(e);
    }

    private async void RenderMarkdownClicked(object sender, RoutedEventArgs e) {
      if ((sender as CheckBox).IsChecked == true) {
        await MarkdownWebView.CoreWebView2.ExecuteScriptAsync($"setRawMode(false);");
      } else {
        await MarkdownWebView.CoreWebView2.ExecuteScriptAsync($"setRawMode(true);");
      }
    }

    private void ModelSelectionChanged(object sender, SelectionChangedEventArgs e) {
      _llmInteractionManager.Options.DefaultModel = ModelSelectionComboBox.SelectedItem.ToString();
    }

#pragma warning restore VSTHRD100 // Avoid async void methods

    #endregion Event Handlers

    #region UI Helpers

    public async Task AskLLM(string message) {
      var fullPrompt = PromptManager.BuildPrompt(message, false, true, false);
      try {
        await _llmInteractionManager.HandleUserMessageAsync(message, fullPrompt);
        ClearError();
      } catch (Exception ex) {
        DisplayError(ex.Message);
      }
    }

    private void DisplayError(string message) {
      RunOnUIThreadAsync(() => {
        ErrorDisplayBorder.Visibility = Visibility.Visible;
        ErrorDisplayTextBlock.Text = message;
        return Task.CompletedTask;
      });
    }

    private void ClearError() {
      RunOnUIThreadAsync(() => {
        ErrorDisplayTextBlock.Text = string.Empty;
        ErrorDisplayBorder.Visibility = Visibility.Collapsed;
        return Task.CompletedTask;
      });
    }

    private void AppendMessageToUI(string message) {
      RunOnUIThreadAsync(() => {
        MarkdownWebView.CoreWebView2.PostWebMessageAsString(message);
        return Task.CompletedTask;
      });
    }

    private void AppendMessageToLog(string message) {
      RunOnUIThreadAsync(() => {
        LogListBox.Items.Add($"{DateTime.Now}:\n{message}");
        return Task.CompletedTask;
      });
    }

    #endregion UI Helpers

    #region WebView2

    private void MarkdownWebViewNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e) {
      var uri = e.Uri;

      // If it's not our original page, block and open externally
      if (!string.IsNullOrEmpty(uri) && !uri.StartsWith("data:") && !uri.StartsWith("about:") && !uri.StartsWith("file://")) {
        e.Cancel = true;
        Process.Start(new ProcessStartInfo {
          FileName = uri,
          UseShellExecute = true
        });
      }
    }

    private async Task InitializeWebViewAsync(WebView2 webView) {
      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

      string userDataFolder = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "OllamaCodeAssistant", "WebView2UserData");

      var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

      await webView.EnsureCoreWebView2Async(env);

      webView.CoreWebView2InitializationCompleted += (s, e) => {
        if (!e.IsSuccess) {
          DisplayError($"WebView2 init failed: {e.InitializationException.Message}");
        }
      };

      webView.NavigateToString(LoadHtmlFromResource());
    }

    private string LoadHtmlFromResource() {
      var assembly = Assembly.GetExecutingAssembly();
      var resourceName = "OllamaCodeAssistant.Resources.ChatView.html";

      using (var stream = assembly.GetManifestResourceStream(resourceName))
      using (var reader = new StreamReader(stream)) {
        return reader.ReadToEnd();
      }
    }

    #endregion WebView2

    #region Chat Logic

    private async Task HandleSubmitButtonClickAsync() {
      ClearError();

      try {
        if (_llmInteractionManager.IsRequestActive) {
          _llmInteractionManager.CancelCurrentRequest();
          return;
        }

        var userPrompt = UserInputTextBox.Text;
        if (string.IsNullOrWhiteSpace(userPrompt)) return;

        // Update the UI to indicate processing
        UserInputTextBox.Clear();
        SubmitButton.Content = "Stop";

        var fullPrompt = PromptManager.BuildPrompt(
          userPrompt,
          ContextIncludeSelection.IsChecked == true,
          ContextIncludeFile.IsChecked == true,
          ContextIncludeAllOpenFile.IsChecked == true);

        await _llmInteractionManager.HandleUserMessageAsync(userPrompt, fullPrompt);
      } catch (Exception ex) {
        DisplayError(ex.Message);
      } finally {
        // Restore UI for next prompt
        UserInputTextBox.IsEnabled = true;
        UserInputTextBox.Focus();
        SubmitButton.Content = "Send";
      }
    }

    #endregion Chat Logic

    private async Task PopulateModelSelectionComboBox(OllamaOptionsPage options) {
      try {
        ModelSelectionComboBox.ItemsSource = await OllamaManager.GetAvailableModelsAsync(options.OllamaApiUrl);
        ModelSelectionComboBox.SelectedItem = options.DefaultModel;
      } catch {
        DisplayError("Failed to load models. Please check your Ollama API URL.");
      }
    }

    private static void RunOnUIThreadAsync(Func<Task> asyncAction, [CallerMemberName] string context = null) {
      _ = Task.Run(async () => {
        try {
          await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
          await asyncAction();
        } catch (Exception ex) {
          Debug.WriteLine($"{context ?? nameof(RunOnUIThreadAsync)} failed: {ex.GetBaseException()}");
        }
      });
    }
  }
}