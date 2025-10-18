namespace AzureYamlToGroovy.Models
{
    public class AzurePipeline
    {
        public string? Trigger { get; set; }
        public PoolInfo? Pool { get; set; }
        public Dictionary<string, object>? Variables { get; set; }
        public List<StageInfo> Stages { get; set; } = new();
    }

    public class PoolInfo
    {
        public string? VmImage { get; set; }
    }

    public class StageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? DependsOn { get; set; }
        public string? Condition { get; set; }
        public List<JobInfo> Jobs { get; set; } = new();
        public TemplateReference? Template { get; set; }
    }

    public class JobInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<TaskInfo> Steps { get; set; } = new();
        public TemplateReference? Template { get; set; }
    }

    public class TaskInfo
    {
        public string Task { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public Dictionary<string, object>? Inputs { get; set; }
    }

    public class TemplateReference
    {
        public string Template { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
    }

    public class TemplateDefinition
    {
        public List<ParameterInfo> Parameters { get; set; } = new();
        public List<JobInfo> Jobs { get; set; } = new();
        public List<TaskInfo> Steps { get; set; } = new();
    }

    public class ParameterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public object? Default { get; set; }
    }
}