using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;

namespace OllamaCodeAssistant.Helpers
{
    public class ProjectContext
    {
        public string TargetFramework { get; set; }
        public string LanguageVersion { get; set; }
        public string NullableContext { get; set; }
        public bool IsSdkStyle { get; set; }

        public override string ToString()
        {
            return $"Target Framework: {TargetFramework}\n" +
                   $"C# Language Version: {LanguageVersion}\n" +
                   $"Nullable Reference Types: {NullableContext}\n" +
                   $"SDK-style Project: {(IsSdkStyle ? "Yes" : "No")}";
        }
    }

    public static class ProjectContextHelper
    {
        public static ProjectContext GetProjectContext(EnvDTE.Project dteProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string projectPath = dteProject?.FullName;
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
                return null;

            try
            {
                var projectCollection = new ProjectCollection();
                var msbuildProject = projectCollection.LoadProject(projectPath);

                string targetFramework = GetTargetFramework(msbuildProject);
                string languageVersion = GetValue(msbuildProject, "LangVersion");
                string nullableContext = GetValue(msbuildProject, "Nullable");
                bool isSdkStyle = msbuildProject.Xml.Sdk != null;

                var context = new ProjectContext
                {
                    TargetFramework = targetFramework,
                    LanguageVersion = languageVersion ?? "default",
                    NullableContext = nullableContext ?? "unspecified",
                    IsSdkStyle = isSdkStyle
                };

                projectCollection.UnloadProject(msbuildProject);
                return context;
            }
            catch (Exception ex)
            {
                // Log the exception details for better debugging
                System.Diagnostics.Debug.WriteLine($"Error loading project context: {ex.Message}");
                return null;
            }
        }

        private static string GetTargetFramework(Project project)
        {
            string tf = GetValue(project, "TargetFramework")
                        ?? GetValue(project, "TargetFrameworks")?.Split(';').FirstOrDefault()
                        ?? GetValue(project, "TargetFrameworkVersion");

            // Fallback to a default if no target framework is found
            return tf ?? "unknown";
        }

        private static string GetValue(Project project, string propertyName)
        {
            var value = project.GetPropertyValue(propertyName);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}