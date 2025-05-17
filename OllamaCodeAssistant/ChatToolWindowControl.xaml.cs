using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OllamaCodeAssistant.Options;

namespace OllamaCodeAssistant {

  public partial class ChatToolWindowControl : UserControl {
    private readonly ChatToolWindow _chatToolWindow;
    private readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();
    private IChatClient _chatClient;
    private string _ollamaApiUrl;
    private string _model;
    private CancellationTokenSource _cancellationTokenSource;

    public ChatToolWindowControl(ChatToolWindow chatToolWindow) {
      _chatToolWindow = chatToolWindow;
      InitializeComponent();
      ClearError();
    }

    #region Event Handlers

    private async void SendButtonClicked(object sender, RoutedEventArgs e) => await HandleSendButtonClickAsync(e);

    private async void ControlLoaded(object sender, RoutedEventArgs e) => await InitializeControlAsync();

    private void TextViewTrackerSelectionChanged(object sender, string e) {
      Dispatcher.BeginInvoke((Action)(() => {
        ContextIncludeSelection.IsChecked = e != null && e.Length > 0;
      }));
    }

    #endregion Event Handlers

    #region UI Helpers

    private void DisplayError(string message) {
      Dispatcher.BeginInvoke((Action)(() => {
        ErrorDisplayBorder.Visibility = Visibility.Visible;
        ErrorDisplayTextBlock.Text = message;
      }));
    }

    private void ClearError() {
      Dispatcher.BeginInvoke((Action)(() => {
        ErrorDisplayTextBlock.Text = string.Empty;
        ErrorDisplayBorder.Visibility = Visibility.Collapsed;
      }));
    }

    private void AppendMessageToUI(string message) {
      Dispatcher.BeginInvoke((Action)(() => {
        MarkdownWebView.CoreWebView2.PostWebMessageAsString(message);
      }));
    }

    #endregion UI Helpers

    #region WebView2 Initialization

    private async Task InitializeControlAsync() {
      Loaded -= ControlLoaded; // Unsubscribe from the event to prevent multiple calls
      try {
        await InitializeWebViewAsync(MarkdownWebView);
      } catch (Exception ex) {
        DisplayError($"Error loading WebView2: {ex.Message}");
      }

      TextSelectionListener.SelectionChanged += TextViewTrackerSelectionChanged;

      UserInputTextBox.Focus();
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

    #endregion WebView2 Initialization

    #region Chat Logic

    private async Task HandleSendButtonClickAsync(RoutedEventArgs e) {
      ClearError();

      if (_cancellationTokenSource != null) {
        Debug.WriteLine("Cancellation requested.");
        _cancellationTokenSource.Cancel();
        return;
      }

      var userPrompt = UserInputTextBox.Text;
      if (string.IsNullOrWhiteSpace(userPrompt)) {
        return;
      }

      // Update the UI to indicate processing
      UserInputTextBox.Clear();
      SendButton.Content = "Stop";

      try {
        // Display user message
        Debug.WriteLine($"User: {userPrompt}");
        AppendMessageToUI($"\n\nYou: {userPrompt}");
        AppendMessageToUI($"\n\nAssistant: ");

        // Add context to the prompt
        userPrompt = PromptManager.BuildPrompt(userPrompt, ContextIncludeSelection.IsChecked == true, ContextIncludeFile.IsChecked == true, ContextIncludeAllOpenFile.IsChecked == true);

        // Add our new message to the chat history
        _chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

        // Read the streaming result from the chat client and update the UI
        var fullResponse = new StringBuilder();
        Debug.WriteLine($"Final Prompt: {userPrompt}");

        var package = _chatToolWindow?.Package as OllamaCodeAssistantPackage;
        var options = package?.GetDialogPage(typeof(OllamaOptionsPage)) as OllamaOptionsPage ?? throw new ApplicationException("Unable to load settings");
        string url = options.OllamaApiUrl;
        string model = options.DefaultModel;

        if (url != _ollamaApiUrl || _model != model) {
          // Settings changed, so reinitialize the chat client
          _ollamaApiUrl = url;
          _model = model;

          _chatClient?.Dispose();
          _chatClient = new OllamaChatClient(new Uri(url), model);
        }

        _cancellationTokenSource = new CancellationTokenSource();
        var asyncEnumerable = _chatClient.GetStreamingResponseAsync(_chatHistory, cancellationToken: _cancellationTokenSource.Token);
        var enumerator = asyncEnumerable.GetAsyncEnumerator(_cancellationTokenSource.Token);
        try {
          while (await enumerator.MoveNextAsync()) {
            if (_cancellationTokenSource.Token.IsCancellationRequested) {
              AppendMessageToUI("\n\nQuery Canceled");
              break;
            }

            var response = enumerator.Current;
            if (response == null) {
              AppendMessageToUI("\n\nResponse Was Empty");
              break;
            }

            fullResponse.Append(response.Text);

            AppendMessageToUI(response.Text);
          }
        } catch (OperationCanceledException) {
          AppendMessageToUI("\n\nQuery Canceled");
        } finally {
          await enumerator.DisposeAsync();
        }

        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
      } catch (Exception ex) {
        DisplayError(ex.Message);
      } finally {
        // Restore UI for next prompt
        UserInputTextBox.IsEnabled = true;
        UserInputTextBox.Focus();
        SendButton.Content = "Send";
        _cancellationTokenSource = null;
      }
    }

    #endregion Chat Logic
  }
}