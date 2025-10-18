using System.CommandLine;
using AzureYamlToGroovy.Services;

namespace AzureYamlToGroovy
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Azure YAML to Groovy Pipeline Converter");

            var yamlFileOption = new Option<FileInfo>(
                name: "--yaml",
                description: "Path to the Azure DevOps YAML pipeline file")
            {
                IsRequired = true
            };

            var outputDirOption = new Option<DirectoryInfo>(
                name: "--output",
                description: "Output directory for generated Groovy files")
            {
                IsRequired = true
            };

            rootCommand.AddOption(yamlFileOption);
            rootCommand.AddOption(outputDirOption);

            rootCommand.SetHandler(async (yamlFile, outputDir) =>
            {
                try
                {
                    var converter = new PipelineConverter();
                    await converter.ConvertAsync(yamlFile.FullName, outputDir.FullName);
                    Console.WriteLine($"✅ Conversion completed successfully!");
                    Console.WriteLine($"📁 Output files generated in: {outputDir.FullName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error during conversion: {ex.Message}");
                }
            }, yamlFileOption, outputDirOption);

            return await rootCommand.InvokeAsync(args);
        }
    }
}