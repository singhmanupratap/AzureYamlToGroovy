using System.Text;
using AzureYamlToGroovy.Models;

namespace AzureYamlToGroovy.Services
{
    public class GroovyGeneratorService
    {
        public async Task GenerateGroovyFilesAsync(AzurePipeline pipeline, string outputDirectory, string yamlBasePath = "")
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outputDirectory);

            // Generate Jenkinsfile
            await GenerateJenkinsfileAsync(pipeline, outputDirectory, yamlBasePath);

            // Generate Groovy files for each stage
            foreach (var stage in pipeline.Stages)
            {
                await GenerateStageGroovyAsync(stage, outputDirectory, yamlBasePath);
            }
        }

        private async Task GenerateJenkinsfileAsync(AzurePipeline pipeline, string outputDirectory, string basePath)
        {
            var sb = new StringBuilder();

            sb.AppendLine("pipeline {");
            sb.AppendLine("    agent any");
            sb.AppendLine();

            if (pipeline.Variables != null && pipeline.Variables.Any())
            {
                sb.AppendLine("    environment {");
                foreach (var variable in pipeline.Variables)
                {
                    sb.AppendLine($"        {variable.Key.ToUpper()} = '{variable.Value}'");
                }
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("    stages {");

            foreach (var stage in pipeline.Stages)
            {
                // Handle template references or SourceFile - maintain folder structure
                string groovyPath;
                if (!string.IsNullOrEmpty(stage.SourceFile))
                {
                    // Use SourceFile to maintain original folder structure and naming
                    groovyPath = Path.ChangeExtension(stage.SourceFile, ".groovy");
                }
                else if (stage.Jobs.Any() && stage.Jobs.First().Template != null)
                {
                    var templatePath = stage.Jobs.First().Template?.Template;
                    if (!string.IsNullOrEmpty(templatePath))
                    {
                        // Convert template path from .yml to .groovy while maintaining structure
                        groovyPath = templatePath.Replace(".yml", ".groovy");
                    }
                    else
                    {
                        var stageName = SanitizeName(stage.Name);
                        groovyPath = $"{stageName}.groovy";
                    }
                }
                else
                {
                    var stageName = SanitizeName(stage.Name);
                    groovyPath = $"{stageName}.groovy";
                }

                sb.AppendLine($"        stage('{stage.DisplayName}') {{");
                sb.AppendLine("            steps {");
                sb.AppendLine("                script {");
                sb.AppendLine($"                    def stageScript = load \"${{env.APP_PATH}}/{groovyPath}\"");
                sb.AppendLine($"                    stageScript()");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            var jenkinsfilePath = Path.Combine(outputDirectory, "Jenkinsfile");
            await File.WriteAllTextAsync(jenkinsfilePath, sb.ToString());

            Console.WriteLine($"✅ Generated: Jenkinsfile");
        }

        private async Task GenerateStageGroovyAsync(StageInfo stage, string outputDirectory, string basePath)
        {
            var sb = new StringBuilder();

            // Determine the output file path based on SourceFile or template or stage name
            string groovyFilePath;
            string displayFileName;

            if (!string.IsNullOrEmpty(stage.SourceFile))
            {
                // Use SourceFile to maintain original folder structure and naming
                var groovyPath = Path.ChangeExtension(stage.SourceFile, ".groovy");

                // Create the directory structure in output
                var fullGroovyPath = Path.Combine(outputDirectory, groovyPath);
                var groovyDir = Path.GetDirectoryName(fullGroovyPath);

                if (!string.IsNullOrEmpty(groovyDir))
                {
                    Directory.CreateDirectory(groovyDir);
                }

                groovyFilePath = fullGroovyPath;
                displayFileName = groovyPath;
            }
            else if (stage.Jobs.Any() && stage.Jobs.First().Template != null)
            {
                // Fallback: Handle template reference - maintain folder structure
                var templatePath = stage.Jobs.First().Template?.Template;
                if (!string.IsNullOrEmpty(templatePath))
                {
                    var groovyPath = templatePath.Replace(".yml", ".groovy");

                    // Create the directory structure in output
                    var fullGroovyPath = Path.Combine(outputDirectory, groovyPath);
                    var groovyDir = Path.GetDirectoryName(fullGroovyPath);

                    if (!string.IsNullOrEmpty(groovyDir))
                    {
                        Directory.CreateDirectory(groovyDir);
                    }

                    groovyFilePath = fullGroovyPath;
                    displayFileName = groovyPath;
                }
                else
                {
                    var stageName = SanitizeName(stage.Name);
                    groovyFilePath = Path.Combine(outputDirectory, $"{stageName}.groovy");
                    displayFileName = $"{stageName}.groovy";
                }
            }
            else
            {
                var stageName = SanitizeName(stage.Name);
                groovyFilePath = Path.Combine(outputDirectory, $"{stageName}.groovy");
                displayFileName = $"{stageName}.groovy";
            }

            // Generate the main call() method
            sb.AppendLine("def call() {");
            sb.AppendLine("    def stepNo = 1");
            sb.AppendLine();

            // Collect all tasks from all jobs in the stage
            var allTasks = new List<TaskInfo>();
            foreach (var job in stage.Jobs)
            {
                allTasks.AddRange(job.Steps);
            }

            // Generate method calls for each task
            foreach (var task in allTasks)
            {
                var displayName = !string.IsNullOrEmpty(task.DisplayName) ? task.DisplayName : task.Task;
                var methodName = GenerateMethodName(task);

                sb.AppendLine($"    logStepMessage(\"{displayName}\", stepNo)");
                sb.AppendLine($"    {methodName}()");
                sb.AppendLine("    stepNo = stepNo + 1");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            sb.AppendLine();

            // Generate logging method
            sb.AppendLine("def logStepMessage(displayName, stepNo) {");
            sb.AppendLine("    echo \"[Step ${stepNo}] ${displayName}\"");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate stub methods for each task
            foreach (var task in allTasks)
            {
                var methodName = GenerateMethodName(task);
                var displayName = !string.IsNullOrEmpty(task.DisplayName) ? task.DisplayName : task.Task;

                sb.AppendLine($"def {methodName}() {{");
                sb.AppendLine("    // TODO: Implement");
                sb.AppendLine($"    // {displayName}");

                if (task.Inputs != null && task.Inputs.Any())
                {
                    sb.AppendLine("    // Inputs:");
                    foreach (var input in task.Inputs)
                    {
                        sb.AppendLine($"    //   {input.Key}: {input.Value}");
                    }
                }

                sb.AppendLine("    // Method implementation goes here");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            // Add return statement to make it a proper Groovy script
            sb.AppendLine("return this");

            await File.WriteAllTextAsync(groovyFilePath, sb.ToString());

            Console.WriteLine($"✅ Generated: {displayFileName}");
        }

        private string GenerateMethodName(TaskInfo task)
        {
            string baseName;

            if (!string.IsNullOrEmpty(task.DisplayName))
            {
                baseName = task.DisplayName;
            }
            else if (!string.IsNullOrEmpty(task.Task))
            {
                // Extract method name from task type
                var taskParts = task.Task.Split('@')[0].Split('.');
                baseName = taskParts.LastOrDefault() ?? "unknownTask";
            }
            else
            {
                baseName = "unknownTask";
            }

            // Convert to camelCase and remove special characters
            var methodName = ToCamelCase(baseName);

            // Ensure it starts with a lowercase letter
            if (char.IsUpper(methodName[0]))
            {
                methodName = char.ToLower(methodName[0]) + methodName.Substring(1);
            }

            return methodName;
        }

        private string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "unknownMethod";

            // Remove special characters and split by spaces, dots, hyphens, etc.
            var words = input.Split(new char[] { ' ', '.', '-', '_', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0)
                return "unknownMethod";

            var result = words[0].ToLower();

            for (int i = 1; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    result += char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }

            // Remove any remaining special characters
            result = new string(result.Where(c => char.IsLetterOrDigit(c)).ToArray());

            return string.IsNullOrEmpty(result) ? "unknownMethod" : result;
        }

        private string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unknownStage";

            // Convert to lowercase and replace spaces with underscores
            var sanitized = name.ToLower().Replace(" ", "_").Replace("-", "_");

            // Remove special characters
            sanitized = new string(sanitized.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

            return string.IsNullOrEmpty(sanitized) ? "unknownStage" : sanitized;
        }
    }
}