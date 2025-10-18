using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using AzureYamlToGroovy.Models;

namespace AzureYamlToGroovy.Services
{
    public class YamlParserService
    {
        private readonly IDeserializer _deserializer;

        public YamlParserService()
        {
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public async Task<AzurePipeline> ParsePipelineAsync(string yamlFilePath)
        {
            var yamlContent = await File.ReadAllTextAsync(yamlFilePath);
            var yamlData = _deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
            
            var pipeline = new AzurePipeline();
            
            if (yamlData.ContainsKey("trigger"))
                pipeline.Trigger = yamlData["trigger"]?.ToString();
                
            if (yamlData.ContainsKey("pool") && yamlData["pool"] is Dictionary<object, object> poolData)
            {
                pipeline.Pool = new PoolInfo
                {
                    VmImage = poolData.ContainsKey("vmImage") ? poolData["vmImage"]?.ToString() : null
                };
            }
            
            if (yamlData.ContainsKey("variables"))
                pipeline.Variables = yamlData["variables"] as Dictionary<string, object>;
                
            if (yamlData.ContainsKey("stages") && yamlData["stages"] is List<object> stages)
            {
                pipeline.Stages = await ParseStagesAsync(stages, Path.GetDirectoryName(yamlFilePath) ?? "");
            }
            
            return pipeline;
        }

        public async Task<TemplateDefinition> ParseTemplateAsync(string templatePath)
        {
            var yamlContent = await File.ReadAllTextAsync(templatePath);
            var yamlData = _deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
            
            var template = new TemplateDefinition();
            
            if (yamlData.ContainsKey("parameters") && yamlData["parameters"] is List<object> parameters)
            {
                template.Parameters = ParseParameters(parameters);
            }
            
            if (yamlData.ContainsKey("jobs") && yamlData["jobs"] is List<object> jobs)
            {
                template.Jobs = await ParseJobsAsync(jobs, Path.GetDirectoryName(templatePath) ?? "");
            }
            
            if (yamlData.ContainsKey("steps") && yamlData["steps"] is List<object> steps)
            {
                template.Steps = ParseSteps(steps);
            }
            
            return template;
        }

        private async Task<List<StageInfo>> ParseStagesAsync(List<object> stages, string basePath)
        {
            var result = new List<StageInfo>();
            
            foreach (var stage in stages)
            {
                if (stage is Dictionary<object, object> stageData)
                {
                    var stageInfo = new StageInfo
                    {
                        Name = stageData.ContainsKey("stage") ? stageData["stage"]?.ToString() ?? "" : "",
                        DisplayName = stageData.ContainsKey("displayName") ? stageData["displayName"]?.ToString() ?? "" : "",
                        DependsOn = stageData.ContainsKey("dependsOn") ? stageData["dependsOn"]?.ToString() : null,
                        Condition = stageData.ContainsKey("condition") ? stageData["condition"]?.ToString() : null
                    };
                    
                    if (stageData.ContainsKey("jobs") && stageData["jobs"] is List<object> jobs)
                    {
                        stageInfo.Jobs = await ParseJobsAsync(jobs, basePath);
                    }
                    
                    result.Add(stageInfo);
                }
            }
            
            return result;
        }

        private async Task<List<JobInfo>> ParseJobsAsync(List<object> jobs, string basePath)
        {
            var result = new List<JobInfo>();
            
            foreach (var job in jobs)
            {
                if (job is Dictionary<object, object> jobData)
                {
                    if (jobData.ContainsKey("template"))
                    {
                        // Handle template reference - this is the correct structure for Azure DevOps
                        var templateRef = new TemplateReference
                        {
                            Template = jobData["template"]?.ToString() ?? ""
                        };
                        
                        if (jobData.ContainsKey("parameters"))
                            templateRef.Parameters = jobData["parameters"] as Dictionary<string, object>;
                            
                        var jobInfo = new JobInfo
                        {
                            Template = templateRef,
                            Name = Path.GetFileNameWithoutExtension(templateRef.Template),
                            DisplayName = $"Template: {templateRef.Template}"
                        };
                        
                        // Load and parse the template
                        var templatePath = Path.Combine(basePath, templateRef.Template);
                        if (File.Exists(templatePath))
                        {
                            var templateDef = await ParseTemplateAsync(templatePath);
                            
                            // If template has steps directly, use them
                            if (templateDef.Steps.Any())
                            {
                                jobInfo.Steps = templateDef.Steps;
                            }
                            // If template has jobs, extract steps from the jobs
                            else if (templateDef.Jobs.Any())
                            {
                                var allSteps = new List<TaskInfo>();
                                foreach (var templateJob in templateDef.Jobs)
                                {
                                    allSteps.AddRange(templateJob.Steps);
                                }
                                jobInfo.Steps = allSteps;
                                
                                // Use the first job's display name for better naming
                                if (templateDef.Jobs.First().DisplayName != null)
                                {
                                    jobInfo.DisplayName = templateDef.Jobs.First().DisplayName;
                                }
                            }
                        }
                        
                        result.Add(jobInfo);
                    }
                    else
                    {
                        var jobInfo = new JobInfo();
                        
                        // Handle regular job
                        if (jobData.ContainsKey("job"))
                        {
                            jobInfo.Name = jobData["job"]?.ToString() ?? "";
                            jobInfo.DisplayName = jobData.ContainsKey("displayName") ? jobData["displayName"]?.ToString() ?? "" : "";
                            
                            if (jobData.ContainsKey("steps") && jobData["steps"] is List<object> steps)
                            {
                                jobInfo.Steps = ParseSteps(steps);
                            }
                        }
                        // Handle deployment job
                        else if (jobData.ContainsKey("deployment"))
                        {
                            jobInfo.Name = jobData["deployment"]?.ToString() ?? "";
                            jobInfo.DisplayName = jobData.ContainsKey("displayName") ? jobData["displayName"]?.ToString() ?? "" : "";
                            
                            // Extract steps from deployment job's strategy.runOnce.deploy.steps
                            if (jobData.ContainsKey("strategy") && jobData["strategy"] is Dictionary<object, object> strategy)
                            {
                                if (strategy.ContainsKey("runOnce") && strategy["runOnce"] is Dictionary<object, object> runOnce)
                                {
                                    if (runOnce.ContainsKey("deploy") && runOnce["deploy"] is Dictionary<object, object> deploy)
                                    {
                                        if (deploy.ContainsKey("steps") && deploy["steps"] is List<object> deploySteps)
                                        {
                                            jobInfo.Steps = ParseSteps(deploySteps);
                                        }
                                    }
                                }
                            }
                        }
                        
                        result.Add(jobInfo);
                    }
                }
            }
            
            return result;
        }

        private List<TaskInfo> ParseSteps(List<object> steps)
        {
            var result = new List<TaskInfo>();
            
            foreach (var step in steps)
            {
                if (step is Dictionary<object, object> stepData)
                {
                    var taskInfo = new TaskInfo
                    {
                        Task = stepData.ContainsKey("task") ? stepData["task"]?.ToString() ?? "" : "",
                        DisplayName = stepData.ContainsKey("displayName") ? stepData["displayName"]?.ToString() ?? "" : ""
                    };
                    
                    if (stepData.ContainsKey("inputs"))
                        taskInfo.Inputs = stepData["inputs"] as Dictionary<string, object>;
                        
                    result.Add(taskInfo);
                }
            }
            
            return result;
        }

        private List<ParameterInfo> ParseParameters(List<object> parameters)
        {
            var result = new List<ParameterInfo>();
            
            foreach (var param in parameters)
            {
                if (param is Dictionary<object, object> paramData)
                {
                    var paramInfo = new ParameterInfo
                    {
                        Name = paramData.ContainsKey("name") ? paramData["name"]?.ToString() ?? "" : "",
                        Type = paramData.ContainsKey("type") ? paramData["type"]?.ToString() ?? "" : ""
                    };
                    
                    if (paramData.ContainsKey("default"))
                        paramInfo.Default = paramData["default"];
                        
                    result.Add(paramInfo);
                }
            }
            
            return result;
        }
    }
}