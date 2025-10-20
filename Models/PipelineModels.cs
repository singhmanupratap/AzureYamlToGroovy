namespace AzureYamlToGroovy.Models
{
    public class AzurePipeline
    {
        public Dictionary<string, object>? Parameters { get; set; }
        public Dictionary<string, object>? Variables { get; set; } // Keep for backward compatibility
        public List<StageInfo> Stages { get; set; } = new();
        public List<JobInfo> Jobs { get; set; } = new(); // For pipelines without stages
        public List<TaskInfo> Steps { get; set; } = new(); // For simple pipelines with just steps
    }

    public class StageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<string> DependsOn { get; set; } = new(); // Can be array or single value
        public string? Condition { get; set; }
        public List<JobInfo> Jobs { get; set; } = new();
        public TemplateReference? Template { get; set; }
        public string? SourceFile { get; set; } // Track original YAML file path
    }

    public class JobInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string JobType { get; set; } = "job"; // job, deployment, template
        public List<string> DependsOn { get; set; } = new();
        public string? Condition { get; set; }
        public List<TaskInfo> Steps { get; set; } = new();
        public TemplateReference? Template { get; set; }
        public string? SourceFile { get; set; } // Track original YAML file path

        // Deployment job specific
        public string? Environment { get; set; }
        public string? Strategy { get; set; } // runOnce, rolling, canary
    }
    public class TaskInfo
    {
        public string TaskType { get; set; } = "task"; // task, script, template, checkout, download, etc.
        public string Task { get; set; } = string.Empty; // Keep for backward compatibility
        public string Name { get; set; } = string.Empty; // For task@version format
        public string DisplayName { get; set; } = string.Empty;
        public Dictionary<string, object>? Inputs { get; set; }
        public TemplateReference? Template { get; set; }

        // For script tasks
        public string? Script { get; set; }
        public string? ScriptType { get; set; } // bash, powershell, etc.
    }

    public class TemplateReference
    {
        public string Template { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
    }

    public class TemplateDefinition
    {
        public List<ParameterInfo> Parameters { get; set; } = new();
        public List<StageInfo> Stages { get; set; } = new();
        public List<JobInfo> Jobs { get; set; } = new();
        public List<TaskInfo> Steps { get; set; } = new();
    }

    public class ParameterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public object? Default { get; set; }
        public List<object>? Values { get; set; } // For choice parameters
    }
}