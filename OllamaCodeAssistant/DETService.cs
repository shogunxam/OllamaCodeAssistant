﻿using System;
using System.Collections.Generic;
using System.IO;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace OllamaCodeAssistant {

  public static class DETService {

    public static string GetActiveDocumentText() {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)Package.GetGlobalService(typeof(DTE));
      Document activeDoc = dte?.ActiveDocument;
      TextDocument textDoc = activeDoc?.Object("TextDocument") as TextDocument;
      EditPoint start = textDoc?.StartPoint.CreateEditPoint();
      return start?.GetText(textDoc.EndPoint);
    }

    public static string GetActiveDocumentName() {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)Package.GetGlobalService(typeof(DTE));
      Document activeDoc = dte?.ActiveDocument;
      return activeDoc?.Name;
    }

    public static string GetTruncatedSelectedText(int maxCodeLength) {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
      if (dte?.ActiveDocument?.Selection is TextSelection selection) {
        string text = selection.Text;
        if (string.IsNullOrWhiteSpace(text))
          return null;

        if (text.Length <= maxCodeLength)
          return text;

        // Truncate cleanly at line breaks
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int totalLength = 0;
        List<string> truncatedLines = new List<string>();

        foreach (var line in lines) {
          if (totalLength + line.Length > maxCodeLength)
            break;

          truncatedLines.Add(line);
          totalLength += line.Length + 1; // Include newline
        }

        truncatedLines.Add("// [Truncated due to length limits]");
        return string.Join(Environment.NewLine, truncatedLines);
      }

      return null;
    }

    private static string GetLanguageFromFile() {
      ThreadHelper.ThrowIfNotOnUIThread();
      DTE2 dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
      string fileName = dte?.ActiveDocument?.Name;

      if (fileName == null) return null;

      string ext = Path.GetExtension(fileName).ToLowerInvariant();

      switch (ext) {
        case ".cs": return "C#";
        case ".ts": return "TypeScript";
        case ".js": return "JavaScript";
        case ".cpp": return "C++";
        case ".h": return "C++ header";
        case ".py": return "Python";
        case ".html": return "HTML";
        case ".css": return "CSS";
        case ".json": return "JSON";
        default: return null;
      }
    }

    public static List<(string FileName, string Text)> GetOpenDocuments() {
      var result = new List<(string, string)>();

      ThreadHelper.ThrowIfNotOnUIThread();
      var dte = (DTE)Package.GetGlobalService(typeof(DTE));
      if (dte == null) return result;

      foreach (Document doc in dte.Documents) {
        if (doc.Object("TextDocument") is TextDocument textDoc) {
          EditPoint startPoint = textDoc.StartPoint.CreateEditPoint();
          string content = startPoint.GetText(textDoc.EndPoint);

          result.Add((doc.FullName, content));
        }
      }

      return result;
    }

    public static bool TryGetActiveProject(out Project activeProject) {
      ThreadHelper.ThrowIfNotOnUIThread();
      var dte = (DTE)Package.GetGlobalService(typeof(DTE));
      activeProject = dte?.ActiveDocument?.ProjectItem?.ContainingProject;
      return activeProject != null;
    }

    public static ErrorListItem GetSelectedError() {
      ThreadHelper.ThrowIfNotOnUIThread();

      var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
      var errorItems = dte?.ToolWindows?.ErrorList?.ErrorItems;

      if (errorItems != null && errorItems.Count > 0) {
        var selectedItem = GetSelectedErrorItem(errorItems);

        if (selectedItem != null) {
          return new ErrorListItem(selectedItem);
        }
      }

      return null; // No selected error item found
    }

    private static ErrorItem GetSelectedErrorItem(ErrorItems items) {
      ThreadHelper.ThrowIfNotOnUIThread();

      for (int i = 1; i <= items.Count; i++) // 1-based index
      {
        var item = items.Item(i);
        if (!string.IsNullOrEmpty(item?.Description)) {
          return item; // crude filter — can be refined
        }
      }

      return null;
    }

    public class ErrorListItem {

      public enum Level {
        Message = 1,
        Warning = 2,
        Error = 4
      }

      public Level ErrorLevel { get; }

      public string Description { get; }

      public string FileName { get; }

      public int Line { get; }

      public int Column { get; }

      public string Project { get; }

      public ErrorListItem(ErrorItem errorItem) {
        ErrorLevel = (Level)errorItem.ErrorLevel;
        Description = errorItem.Description;
        FileName = errorItem.FileName;
        Line = errorItem.Line;
        Column = errorItem.Column;
        Project = errorItem.Project;
      }
    }
  }
}