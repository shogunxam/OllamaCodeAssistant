using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;

namespace OllamaCodeAssistant.Helpers
{

    /// <summary>
    /// Represents a context for a Visual Studio project, including its target framework,
    /// language version, nullable reference types setting, and whether it is an SDK-style project.
    /// </summary>
    public class ProjectContext
    {

        // Properties to hold project context information
        public string TargetFramework { get; set; }

        public string LanguageVersion { get; set; }
        public string NullableContext { get; set; }
        public bool IsSdkStyle { get; set; }

        /// <summary>
        /// Provides a formatted string representation of the project context.
        /// </summary>
        /// <returns>A string containing details about the target framework, language version,
        /// nullable reference types, and SDK-style status.</returns>
        public override string ToString()
        {
            return $"Target Framework: {TargetFramework}\n" +
                   $"C# Language Version: {LanguageVersion}\n" +
                   $"Nullable Reference Types: {NullableContext}\n" +
                   $"SDK-style Project: {(IsSdkStyle ? "Yes" : "No")}";
        }
    }

    /// <summary>
    /// Provides helper methods to retrieve and manipulate project context information.
    /// </summary>
    public static class ProjectContextHelper
    {

        /// <summary>
        /// Retrieves the context of a Visual Studio project, including its target framework,
        /// language version, nullable reference types setting, and whether it is an SDK-style project.
        /// </summary>
        /// <param name="dteProject">The EnvDTE.Project object representing the project.</param>
        /// <returns>A ProjectContext object containing the project's context information, or null if an error occurs.</returns>
        public static ProjectContext GetProjectContext(EnvDTE.Project dteProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // Ensure this method is called on the UI thread

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

        /// <summary>
        /// Retrieves the target framework of an MSBuild project.
        /// </summary>
        /// <param name="project">The MSBuild Project object.</param>
        /// <returns>The target framework string, or "unknown" if not found.</returns>
        private static string GetTargetFramework(Project project)
        {
            string tf = GetValue(project, "TargetFramework")
                        ?? GetValue(project, "TargetFrameworks")?.Split(';').FirstOrDefault()
                        ?? GetValue(project, "TargetFrameworkVersion");

            // Fallback to a default if no target framework is found
            return tf ?? "unknown";
        }

        /// <summary>
        /// Retrieves the value of a specified property from an MSBuild project.
        /// </summary>
        /// <param name="project">The MSBuild Project object.</param>
        /// <param name="propertyName">The name of the property to retrieve.</param>
        /// <returns>The property value, or null if it is whitespace or empty.</returns>
        private static string GetValue(Project project, string propertyName)
        {
            var value = project.GetPropertyValue(propertyName);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}