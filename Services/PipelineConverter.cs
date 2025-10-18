using AzureYamlToGroovy.Models;

namespace AzureYamlToGroovy.Services
{
    public class PipelineConverter
    {
        private readonly YamlParserService _yamlParser;
        private readonly GroovyGeneratorService _groovyGenerator;

        public PipelineConverter()
        {
            _yamlParser = new YamlParserService();
            _groovyGenerator = new GroovyGeneratorService();
        }

        public async Task ConvertAsync(string yamlFilePath, string outputDirectory)
        {
            if (!File.Exists(yamlFilePath))
            {
                throw new FileNotFoundException($"YAML file not found: {yamlFilePath}");
            }

            Console.WriteLine($"🔄 Starting conversion of: {yamlFilePath}");
            Console.WriteLine($"📂 Output directory: {outputDirectory}");
            Console.WriteLine();

            // Parse the Azure DevOps YAML pipeline
            Console.WriteLine("📖 Parsing Azure DevOps YAML pipeline...");
            var pipeline = await _yamlParser.ParsePipelineAsync(yamlFilePath);
            
            Console.WriteLine($"✅ Found {pipeline.Stages.Count} stage(s):");
            foreach (var stage in pipeline.Stages)
            {
                Console.WriteLine($"   - {stage.DisplayName} ({stage.Jobs.Count} job(s))");
                foreach (var job in stage.Jobs)
                {
                    Console.WriteLine($"     └─ {job.DisplayName} ({job.Steps.Count} step(s))");
                }
            }
            Console.WriteLine();

            // Generate Groovy scripts
            Console.WriteLine("🔧 Generating Groovy scripts...");
            var yamlBasePath = Path.GetDirectoryName(yamlFilePath) ?? "";
            await _groovyGenerator.GenerateGroovyFilesAsync(pipeline, outputDirectory, yamlBasePath);
            Console.WriteLine();

            // Rename YAML files to .delete.yml
            Console.WriteLine("📝 Renaming original YAML files to .delete.yml...");
            await RenameYamlFilesToDelete(yamlBasePath);
            Console.WriteLine();

            // Display summary
            DisplayConversionSummary(outputDirectory);
        }

        private void DisplayConversionSummary(string outputDirectory)
        {
            Console.WriteLine("📋 Conversion Summary:");
            Console.WriteLine("===================");
            
            var files = Directory.GetFiles(outputDirectory, "*.*", SearchOption.AllDirectories);
            
            foreach (var file in files.OrderBy(f => f))
            {
                var fileName = Path.GetFileName(file);
                var fileSize = new FileInfo(file).Length;
                Console.WriteLine($"   📄 {fileName} ({fileSize} bytes)");
            }
            
            Console.WriteLine();
            Console.WriteLine("🚀 Next Steps:");
            Console.WriteLine("   1. Review the generated Groovy files");
            Console.WriteLine("   2. Implement the method stubs with actual logic");
            Console.WriteLine("   3. Test the Jenkinsfile in your Jenkins environment");
            Console.WriteLine("   4. Adjust the pipeline as needed for your specific requirements");
        }

        private async Task RenameYamlFilesToDelete(string yamlBasePath)
        {
            var yamlFiles = Directory.GetFiles(yamlBasePath, "*.yml", SearchOption.AllDirectories)
                                    .Where(f => !f.EndsWith(".delete.yml"))
                                    .ToArray();
            
            foreach (var yamlFile in yamlFiles)
            {
                var deleteFile = yamlFile.Replace(".yml", ".delete.yml");
                if (!File.Exists(deleteFile))
                {
                    File.Move(yamlFile, deleteFile);
                    var relativePath = Path.GetRelativePath(yamlBasePath, deleteFile);
                    Console.WriteLine($"✅ Renamed: {relativePath}");
                }
            }
        }
    }
}