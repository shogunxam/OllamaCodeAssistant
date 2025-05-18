using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using OllamaCodeAssistant.Options;
using WebView2 = Microsoft.Web.WebView2.Wpf.WebView2;

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

      OllamaAsyncQuickInfoSource.ChatToolWindowControl = this;

      InitializeComponent();
      ClearError();
    }

    #region Event Handlers

    private async void SubmitButtonClicked(object sender, RoutedEventArgs e) => await HandleSubmitButtonClickAsync(e);

    private async void ControlLoaded(object sender, RoutedEventArgs e) => await InitializeControlAsync();

    private void TextViewTrackerSelectionChanged(object sender, string e) {
      Dispatcher.BeginInvoke((Action)(() => {
        ContextIncludeSelection.IsChecked = e != null && e.Length > 0;
      }));
    }

    private async void RenderMarkdownClicked(object sender, RoutedEventArgs e) {
      if ((sender as CheckBox).IsChecked == true) {
        await MarkdownWebView.CoreWebView2.ExecuteScriptAsync($"setRawMode(false);");
      } else {
        await MarkdownWebView.CoreWebView2.ExecuteScriptAsync($"setRawMode(true);");
      }
    }

    #endregion Event Handlers

    #region UI Helpers

    public void AskLLM(string message) {
      UserInputTextBox.Text = message;
      HandleSubmitButtonClickAsync(null);
    }

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

    private async Task HandleSubmitButtonClickAsync(RoutedEventArgs e) {
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
      SubmitButton.Content = "Stop";

      try {
        // Display user message
        Debug.WriteLine($"User: {userPrompt}");
        AppendMessageToUI($"\n\nYou: {userPrompt}");
        AppendMessageToUI($"\n\nAssistant: ");

        // Add context to the prompt
        userPrompt = PromptManager.BuildPrompt(userPrompt, ContextIncludeSelection.IsChecked == true, ContextIncludeFile.IsChecked == true, ContextIncludeAllOpenFile.IsChecked == true);

        // Add our new message to the chat history
        _chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));
        Debug.WriteLine($"Final Prompt: {userPrompt}");

        OllamaOptionsPage options = GetExtensionOptions();
        string url = options.OllamaApiUrl;
        string model = options.DefaultModel;

        if (url != _ollamaApiUrl || _model != model) {
          // Settings changed, so reinitialize the chat client
          _ollamaApiUrl = url;
          _model = model;

          _chatClient?.Dispose();
          _chatClient = new OllamaChatClient(new Uri(url), model);
        }

        // Read the streaming result from the chat client and update the UI
        var fullResponse = new StringBuilder();
        _cancellationTokenSource = new CancellationTokenSource();
        var asyncEnumerable = _chatClient.GetStreamingResponseAsync(_chatHistory, cancellationToken: _cancellationTokenSource.Token);
        var enumerator = asyncEnumerable.GetAsyncEnumerator(_cancellationTokenSource.Token);
        try {
          while (await enumerator.MoveNextAsync()) {
            if (_cancellationTokenSource.Token.IsCancellationRequested) {
              break;
            }

            // Append the response to the UI and the full response
            var response = enumerator.Current ?? throw new ApplicationException("Chat response was null");
            fullResponse.Append(response.Text);
            AppendMessageToUI(response.Text);
          }
        } catch (OperationCanceledException) {
          // Handle cancellation gracefully
        } finally {
          await enumerator.DisposeAsync();
        }

        // Ensure code block is closed if needed
        int codeBlockCount = Regex.Matches(fullResponse.ToString(), "```").Count;
        if (codeBlockCount % 2 != 0) {
          AppendMessageToUI("\n```");
          fullResponse.Append("\n```");
        }

        // Report if the query was canceled
        if (_cancellationTokenSource.Token.IsCancellationRequested) {
          fullResponse.Append("\n\n...This response was cut short because the user canceled the request.\n\n");
          AppendMessageToUI("\n\nQuery Canceled");
        }

        // Add the assistant's response to the chat history
        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
      } catch (Exception ex) {
        DisplayError(ex.Message);
      } finally {
        // Restore UI for next prompt
        UserInputTextBox.IsEnabled = true;
        UserInputTextBox.Focus();
        SubmitButton.Content = "Send";
        _cancellationTokenSource = null;
      }
    }

    #endregion Chat Logic

    #region Helpers

    private OllamaOptionsPage GetExtensionOptions() {
      var package = _chatToolWindow?.Package as OllamaCodeAssistantPackage;
      var options = package?.GetDialogPage(typeof(OllamaOptionsPage)) as OllamaOptionsPage ?? throw new ApplicationException("Unable to load settings");
      return options;
    }

    #endregion Helpers
  }
}