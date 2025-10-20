using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using AzureYamlToGroovy.Models;

namespace AzureYamlToGroovy.Services
{
    public class YamlParserService
    {
        private readonly IDeserializer _deserializer;
        private readonly HashSet<string> _processedFiles = new(); // Prevent infinite recursion
        private string _baseDirectory = string.Empty; // Track base directory for relative paths

        public YamlParserService()
        {
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public async Task<AzurePipeline> ParsePipelineAsync(string yamlFilePath)
        {
            _processedFiles.Clear();
            var fullPath = Path.GetFullPath(yamlFilePath);
            _baseDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var yamlContent = await File.ReadAllTextAsync(fullPath);

            var pipeline = new AzurePipeline();

            // Try to parse as object first
            try
            {
                var yamlData = _deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
                await ParsePipelineFromObjectAsync(yamlData, pipeline, Path.GetDirectoryName(fullPath) ?? "");
            }
            catch
            {
                // If that fails, try to parse as array (direct stages/jobs/steps)
                try
                {
                    var yamlArray = _deserializer.Deserialize<List<object>>(yamlContent);
                    await ParsePipelineFromArrayAsync(yamlArray, pipeline, Path.GetDirectoryName(fullPath) ?? "");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to parse YAML file: {yamlFilePath}. Error: {ex.Message}");
                }
            }

            return pipeline;
        }

        private async Task ParsePipelineFromObjectAsync(Dictionary<string, object> yamlData, AzurePipeline pipeline, string basePath)
        {
            // Parse parameters
            if (yamlData.ContainsKey("parameters"))
                pipeline.Parameters = yamlData["parameters"] as Dictionary<string, object>;

            // Parse variables (for backward compatibility)
            if (yamlData.ContainsKey("variables"))
                pipeline.Variables = yamlData["variables"] as Dictionary<string, object>;

            // Handle different pipeline structures according to Azure DevOps hierarchy
            if (yamlData.ContainsKey("stages"))
            {
                pipeline.Stages = await ParseStagesAsync(yamlData["stages"], basePath);
            }
            else if (yamlData.ContainsKey("jobs"))
            {
                pipeline.Jobs = await ParseJobsAsync(yamlData["jobs"], basePath);
            }
            else if (yamlData.ContainsKey("steps"))
            {
                pipeline.Steps = ParseStepsAsync(yamlData["steps"], basePath);
            }
        }

        private async Task ParsePipelineFromArrayAsync(List<object> yamlArray, AzurePipeline pipeline, string basePath)
        {
            // Determine what type of array this is by examining the first element
            if (yamlArray.Any() && yamlArray[0] is Dictionary<object, object> firstItem)
            {
                if (firstItem.ContainsKey("stage") || firstItem.ContainsKey("template"))
                {
                    // Array of stages
                    pipeline.Stages = await ParseStagesAsync(yamlArray, basePath);
                }
                else if (firstItem.ContainsKey("job") || firstItem.ContainsKey("deployment") || firstItem.ContainsKey("template"))
                {
                    // Array of jobs
                    pipeline.Jobs = await ParseJobsAsync(yamlArray, basePath);
                }
                else
                {
                    // Array of steps
                    pipeline.Steps = ParseStepsAsync(yamlArray, basePath);
                }
            }
        }

        private async Task<List<StageInfo>> ParseStagesAsync(object stagesData, string basePath)
        {
            var result = new List<StageInfo>();

            if (stagesData is List<object> stagesList)
            {
                foreach (var stageItem in stagesList)
                {
                    if (stageItem is Dictionary<object, object> stageDict)
                    {
                        if (stageDict.ContainsKey("template"))
                        {
                            // Template reference
                            var templateStages = await ExpandTemplateToStagesAsync(stageDict, basePath);
                            result.AddRange(templateStages);
                        }
                        else if (stageDict.ContainsKey("stage"))
                        {
                            // Regular stage
                            var stage = await ParseSingleStageAsync(stageDict, basePath);
                            result.Add(stage);
                        }
                    }
                }
            }
            else if (stagesData is Dictionary<object, object> stagesDict)
            {
                if (stagesDict.ContainsKey("template"))
                {
                    // Single template reference for all stages
                    var templateStages = await ExpandTemplateToStagesAsync(stagesDict, basePath);
                    result.AddRange(templateStages);
                }
            }

            return result;
        }

        private async Task<List<StageInfo>> ExpandTemplateToStagesAsync(Dictionary<object, object> templateRef, string basePath)
        {
            var templatePath = templateRef["template"]?.ToString() ?? "";
            var parameters = templateRef.ContainsKey("parameters") ?
                templateRef["parameters"] as Dictionary<string, object> : null;

            var fullTemplatePath = Path.Combine(basePath, templatePath);
            var normalizedPath = Path.GetFullPath(fullTemplatePath);

            if (_processedFiles.Contains(normalizedPath))
            {
                // Prevent infinite recursion
                return new List<StageInfo> { CreateErrorStage(templatePath, "Circular reference detected") };
            }

            if (!File.Exists(normalizedPath))
            {
                return new List<StageInfo> { CreateErrorStage(templatePath, "Template file not found") };
            }

            try
            {
                _processedFiles.Add(normalizedPath);
                var templateContent = await File.ReadAllTextAsync(normalizedPath);
                var templateBasePath = Path.GetDirectoryName(normalizedPath) ?? "";

                // Try parsing as object first
                try
                {
                    var templateData = _deserializer.Deserialize<Dictionary<string, object>>(templateContent);

                    if (templateData.ContainsKey("stages"))
                    {
                        return await ParseStagesAsync(templateData["stages"], templateBasePath);
                    }
                    else if (templateData.ContainsKey("jobs"))
                    {
                        // Template contains jobs, wrap in a stage
                        var jobs = await ParseJobsAsync(templateData["jobs"], templateBasePath);
                        // Calculate relative path from base directory
                        var relativeSourceFile = Path.GetRelativePath(_baseDirectory, normalizedPath);
                        return new List<StageInfo>
                        {
                            new StageInfo
                            {
                                Name = Path.GetFileNameWithoutExtension(templatePath),
                                DisplayName = $"Template: {templatePath}",
                                Jobs = jobs,
                                Template = new TemplateReference { Template = templatePath, Parameters = parameters },
                                SourceFile = relativeSourceFile
                            }
                        };
                    }
                    else if (templateData.ContainsKey("steps"))
                    {
                        // Template contains steps, wrap in job then stage
                        var templateSteps = ParseStepsAsync(templateData["steps"], templateBasePath);
                        var job = new JobInfo
                        {
                            Name = "TemplateSteps",
                            DisplayName = $"Steps from {templatePath}",
                            Steps = templateSteps
                        };
                        // Calculate relative path from base directory
                        var relativeSourceFile = Path.GetRelativePath(_baseDirectory, normalizedPath);
                        return new List<StageInfo>
                        {
                            new StageInfo
                            {
                                Name = Path.GetFileNameWithoutExtension(templatePath),
                                DisplayName = $"Template: {templatePath}",
                                Jobs = new List<JobInfo> { job },
                                Template = new TemplateReference { Template = templatePath, Parameters = parameters },
                                SourceFile = relativeSourceFile
                            }
                        };
                    }
                }
                catch
                {
                    // Try parsing as array (direct stages)
                    var templateArray = _deserializer.Deserialize<List<object>>(templateContent);
                    return await ParseStagesAsync(templateArray, templateBasePath);
                }

                return new List<StageInfo> { CreateErrorStage(templatePath, "No stages, jobs, or steps found in template") };
            }
            catch (Exception ex)
            {
                return new List<StageInfo> { CreateErrorStage(templatePath, $"Error parsing template: {ex.Message}") };
            }
            finally
            {
                _processedFiles.Remove(normalizedPath);
            }
        }

        private async Task<StageInfo> ParseSingleStageAsync(Dictionary<object, object> stageData, string basePath)
        {
            var stage = new StageInfo
            {
                Name = stageData.ContainsKey("stage") ? stageData["stage"]?.ToString() ?? "" : "",
                DisplayName = stageData.ContainsKey("displayName") ? stageData["displayName"]?.ToString() ?? "" : "",
                DependsOn = ParseDependsOn(stageData.ContainsKey("dependsOn") ? stageData["dependsOn"] : null),
                Condition = stageData.ContainsKey("condition") ? stageData["condition"]?.ToString() : null
            };

            if (stageData.ContainsKey("jobs"))
            {
                stage.Jobs = await ParseJobsAsync(stageData["jobs"], basePath);

                // If any job has a SourceFile from template expansion, use it for the stage
                var jobWithSourceFile = stage.Jobs.FirstOrDefault(j => !string.IsNullOrEmpty(j.SourceFile));
                if (jobWithSourceFile != null)
                {
                    stage.SourceFile = jobWithSourceFile.SourceFile;
                }
            }

            return stage;
        }

        private async Task<List<JobInfo>> ParseJobsAsync(object jobsData, string basePath)
        {
            var result = new List<JobInfo>();

            if (jobsData is List<object> jobsList)
            {
                foreach (var jobItem in jobsList)
                {
                    if (jobItem is Dictionary<object, object> jobDict)
                    {
                        if (jobDict.ContainsKey("template"))
                        {
                            // Template reference
                            var templateJobs = await ExpandTemplateToJobsAsync(jobDict, basePath);
                            result.AddRange(templateJobs);
                        }
                        else if (jobDict.ContainsKey("job"))
                        {
                            // Regular job
                            var job = await ParseSingleJobAsync(jobDict, basePath, "job");
                            result.Add(job);
                        }
                        else if (jobDict.ContainsKey("deployment"))
                        {
                            // Deployment job
                            var job = await ParseSingleJobAsync(jobDict, basePath, "deployment");
                            result.Add(job);
                        }
                    }
                }
            }

            return result;
        }

        private async Task<List<JobInfo>> ExpandTemplateToJobsAsync(Dictionary<object, object> templateRef, string basePath)
        {
            var templatePath = templateRef["template"]?.ToString() ?? "";
            var parameters = templateRef.ContainsKey("parameters") ?
                templateRef["parameters"] as Dictionary<string, object> : null;

            var fullTemplatePath = Path.Combine(basePath, templatePath);
            var normalizedPath = Path.GetFullPath(fullTemplatePath);

            if (_processedFiles.Contains(normalizedPath))
            {
                return new List<JobInfo> { CreateErrorJob(templatePath, "Circular reference detected") };
            }

            if (!File.Exists(normalizedPath))
            {
                return new List<JobInfo> { CreateErrorJob(templatePath, "Template file not found") };
            }

            try
            {
                _processedFiles.Add(normalizedPath);
                var templateContent = await File.ReadAllTextAsync(normalizedPath);
                var templateBasePath = Path.GetDirectoryName(normalizedPath) ?? "";

                // Try parsing as object first
                try
                {
                    var templateData = _deserializer.Deserialize<Dictionary<string, object>>(templateContent);

                    if (templateData.ContainsKey("jobs"))
                    {
                        return await ParseJobsAsync(templateData["jobs"], templateBasePath);
                    }
                    else if (templateData.ContainsKey("steps"))
                    {
                        // Template contains steps, wrap in a job
                        var steps = ParseStepsAsync(templateData["steps"], templateBasePath);
                        return new List<JobInfo>
                        {
                            new JobInfo
                            {
                                Name = Path.GetFileNameWithoutExtension(templatePath),
                                DisplayName = $"Template: {templatePath}",
                                JobType = "template",
                                Steps = steps,
                                Template = new TemplateReference { Template = templatePath, Parameters = parameters },
                                SourceFile = Path.GetRelativePath(_baseDirectory, normalizedPath)
                            }
                        };
                    }
                }
                catch
                {
                    // Try parsing as array (direct jobs)
                    var templateArray = _deserializer.Deserialize<List<object>>(templateContent);
                    return await ParseJobsAsync(templateArray, templateBasePath);
                }

                return new List<JobInfo> { CreateErrorJob(templatePath, "No jobs or steps found in template") };
            }
            catch (Exception ex)
            {
                return new List<JobInfo> { CreateErrorJob(templatePath, $"Error parsing template: {ex.Message}") };
            }
            finally
            {
                _processedFiles.Remove(normalizedPath);
            }
        }

        private async Task<JobInfo> ParseSingleJobAsync(Dictionary<object, object> jobData, string basePath, string jobType)
        {
            var jobKey = jobType == "deployment" ? "deployment" : "job";

            var job = new JobInfo
            {
                Name = jobData.ContainsKey(jobKey) ? jobData[jobKey]?.ToString() ?? "" : "",
                DisplayName = jobData.ContainsKey("displayName") ? jobData["displayName"]?.ToString() ?? "" : "",
                JobType = jobType,
                DependsOn = ParseDependsOn(jobData.ContainsKey("dependsOn") ? jobData["dependsOn"] : null),
                Condition = jobData.ContainsKey("condition") ? jobData["condition"]?.ToString() : null,
                Environment = jobData.ContainsKey("environment") ? jobData["environment"]?.ToString() : null
            };

            // Handle steps
            if (jobData.ContainsKey("steps"))
            {
                job.Steps = ParseStepsAsync(jobData["steps"], basePath);
            }
            else if (jobType == "deployment" && jobData.ContainsKey("strategy"))
            {
                // Handle deployment strategy steps
                var strategy = jobData["strategy"] as Dictionary<object, object>;
                if (strategy != null && strategy.ContainsKey("runOnce"))
                {
                    job.Strategy = "runOnce";
                    var runOnce = strategy["runOnce"] as Dictionary<object, object>;
                    if (runOnce != null && runOnce.ContainsKey("deploy"))
                    {
                        var deploy = runOnce["deploy"] as Dictionary<object, object>;
                        if (deploy != null && deploy.ContainsKey("steps"))
                        {
                            job.Steps = ParseStepsAsync(deploy["steps"], basePath);
                        }
                    }
                }
            }

            return job;
        }

        private List<TaskInfo> ParseStepsAsync(object stepsData, string basePath)
        {
            var result = new List<TaskInfo>();

            if (stepsData is List<object> stepsList)
            {
                foreach (var stepItem in stepsList)
                {
                    if (stepItem is Dictionary<object, object> stepDict)
                    {
                        // Steps can only be regular tasks, scripts, etc. - NOT templates
                        // Template references are only valid at stage and job levels
                        var step = ParseSingleStep(stepDict);
                        result.Add(step);
                    }
                }
            }

            return result;
        }

        private TaskInfo ParseSingleStep(Dictionary<object, object> stepData)
        {
            var task = new TaskInfo();

            if (stepData.ContainsKey("template"))
            {
                // Templates are not valid at step level in Azure DevOps
                task.TaskType = "error";
                task.DisplayName = $"Invalid template reference: {stepData["template"]} (templates not allowed at step level)";
                task.Task = "";
                return task;
            }
            else if (stepData.ContainsKey("task"))
            {
                // Regular task
                task.TaskType = "task";
                task.Task = stepData["task"]?.ToString() ?? "";
                task.Name = stepData["task"]?.ToString() ?? "";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "" : "";
                task.Inputs = stepData.ContainsKey("inputs") ?
                    stepData["inputs"] as Dictionary<string, object> : null;
            }
            else if (stepData.ContainsKey("script"))
            {
                // Script task
                task.TaskType = "script";
                task.Script = stepData["script"]?.ToString() ?? "";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "Script" : "Script";
                task.ScriptType = stepData.ContainsKey("scriptType") ?
                    stepData["scriptType"]?.ToString() : "bash";
            }
            else if (stepData.ContainsKey("powershell"))
            {
                task.TaskType = "script";
                task.Script = stepData["powershell"]?.ToString() ?? "";
                task.ScriptType = "powershell";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "PowerShell" : "PowerShell";
            }
            else if (stepData.ContainsKey("bash"))
            {
                task.TaskType = "script";
                task.Script = stepData["bash"]?.ToString() ?? "";
                task.ScriptType = "bash";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "Bash" : "Bash";
            }
            else if (stepData.ContainsKey("pwsh"))
            {
                task.TaskType = "script";
                task.Script = stepData["pwsh"]?.ToString() ?? "";
                task.ScriptType = "pwsh";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "PowerShell Core" : "PowerShell Core";
            }
            else if (stepData.ContainsKey("checkout"))
            {
                task.TaskType = "checkout";
                task.Name = "checkout";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "Checkout" : "Checkout";
                task.Inputs = new Dictionary<string, object>
                {
                    ["repository"] = stepData["checkout"]?.ToString() ?? "self"
                };
            }
            else if (stepData.ContainsKey("download"))
            {
                task.TaskType = "download";
                task.Name = "download";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "Download" : "Download";
                task.Inputs = new Dictionary<string, object>
                {
                    ["artifact"] = stepData["download"]?.ToString() ?? ""
                };
            }
            else
            {
                // Unknown step type
                task.TaskType = "unknown";
                task.DisplayName = stepData.ContainsKey("displayName") ?
                    stepData["displayName"]?.ToString() ?? "Unknown Step" : "Unknown Step";
            }

            return task;
        }

        private List<string> ParseDependsOn(object? dependsOnData)
        {
            if (dependsOnData == null)
                return new List<string>();

            if (dependsOnData is List<object> dependsList)
            {
                return dependsList.Select(d => d?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
            }
            else if (dependsOnData is string dependsString)
            {
                return new List<string> { dependsString };
            }

            return new List<string>();
        }

        private StageInfo CreateErrorStage(string templatePath, string error)
        {
            return new StageInfo
            {
                Name = Path.GetFileNameWithoutExtension(templatePath),
                DisplayName = $"Template: {templatePath} ({error})",
                Jobs = new List<JobInfo>()
            };
        }

        private JobInfo CreateErrorJob(string templatePath, string error)
        {
            return new JobInfo
            {
                Name = Path.GetFileNameWithoutExtension(templatePath),
                DisplayName = $"Template: {templatePath} ({error})",
                JobType = "error",
                Steps = new List<TaskInfo>()
            };
        }

        private TaskInfo CreateErrorTask(string templatePath, string error)
        {
            return new TaskInfo
            {
                TaskType = "error",
                DisplayName = $"Template: {templatePath} ({error})",
                Task = ""
            };
        }
    }
}