using System.IO;
using System.Linq;
using System.Text;
using OllamaCodeAssistant.Helpers;
using static OllamaCodeAssistant.DETService;

namespace OllamaCodeAssistant {

  public static class PromptManager {
    private const int MaxCodeLength = 1500;

    public static string BuildPrompt(string userInput, bool includeSelection, bool includeFile, bool includeOpenFiles) {
      Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
      ProjectContext projectContext = null;
      if (DETService.TryGetActiveProject(out var activeProject)) {
        projectContext = ProjectContextHelper.GetProjectContext(activeProject);
      }

      var finalPrompt = new StringBuilder();

      finalPrompt.AppendLine("You are an AI code assistant running in Visual Studio.");
      if (activeProject != null) {
        finalPrompt.AppendLine($"The active project is named '{activeProject.Name}' and has the following properties:");
        finalPrompt.AppendLine(projectContext.ToString());
      } else {
        finalPrompt.AppendLine("The user is editing code in a Visual Studio project.");
      }

      finalPrompt.AppendLine("\n\n");

      // Include the users selected context
      if (includeSelection) {
        finalPrompt.AppendLine($"Here is the relevant code:\n\n```{DETService.GetTruncatedSelectedText(MaxCodeLength)}```");
      } else if (includeFile) {
        finalPrompt.AppendLine($"The relevant code is in file '{DETService.GetActiveDocumentName()}' which contains :\n\n```{DETService.GetActiveDocumentText()}```");
      } else if (includeOpenFiles) {
        finalPrompt.AppendLine("Please use the following files for context:");
        foreach (var doc in DETService.GetOpenDocuments()) {
          finalPrompt.AppendLine($"File '{doc.FileName}':\n```{doc.Text}```\n\n");
        }
      }

      finalPrompt.AppendLine("\n\n### USER REQUEST");

      // Expand the user's prompt to include common commands
      string normalizedUserInput = userInput.Trim().ToLowerInvariant();
      if (normalizedUserInput == "explain" || normalizedUserInput.StartsWith("explain this")) {
        finalPrompt.AppendLine("Please explain what this code does.");
      } else if (normalizedUserInput.StartsWith("refactor")) {
        finalPrompt.AppendLine("Refactor this code to be cleaner or more efficient.");
      } else if (normalizedUserInput.StartsWith("add comments") || normalizedUserInput.Contains("document")) {
        finalPrompt.AppendLine("Add inline comments to explain the logic in this code.");
      } else {
        finalPrompt.AppendLine(userInput);
      }

      return finalPrompt.ToString();
    }

    public static string BuildPrompt(ErrorListItem errorListItem) {
      var result = new StringBuilder();

      result.AppendLine($"You are an AI code assistant running in Visual Studio.");
      result.AppendLine($"The compiler is reporting an '{errorListItem.ErrorLevel}' with the description '{errorListItem.Description}'");
      result.AppendLine($"The error description is in file '{errorListItem.FileName}' which is part of the project '{errorListItem.Project}'");
      result.AppendLine($"");
      result.AppendLine($"Here is the contents of the file:");
      result.AppendLine($"```{File.ReadAllText(errorListItem.FileName)}```");
      result.AppendLine("");
      result.AppendLine($"The error is on line {errorListItem.Line} column {errorListItem.Column}.");
      result.AppendLine($"Here is that line of code:");
      result.AppendLine($"```{File.ReadLines(errorListItem.FileName).Skip(errorListItem.Line - 1).FirstOrDefault()}```");
      result.AppendLine("");
      result.AppendLine($"I need you to provide a solution to this error.");

      return result.ToString();
    }
  }
}