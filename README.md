# Azure YAML to Groovy Converter

A .NET command-line tool that converts Azure DevOps YAML pipelines to Jenkins Groovy scripts while maintaining folder structure and template references.

## Features

- ✅ **Template Structure Preservation**: Maintains `templates/` folder hierarchy
- ✅ **Multiple Job Types**: Supports regular jobs, template references, and deployment jobs
- ✅ **YAML File Management**: Automatically renames original files to `.delete.yml`
- ✅ **Environment Variable Support**: Uses `${env.APP_PATH}` for flexible template paths
- ✅ **Method Stub Generation**: Creates TODO method stubs for all pipeline tasks

## Prerequisites

- .NET 9.0 or later
- Windows, macOS, or Linux

## Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd AzureYamlToGroovy
```

2. Build the project:
```bash
dotnet build
```

## Usage

### Basic Command
```bash
dotnet run -- --yaml <path-to-yaml-file> --output <output-directory>
```

### Example
```bash
dotnet run -- --yaml "C:\MyProject\azure-pipelines.yml" --output "C:\MyProject\jenkins"
```

## Input Structure

The tool expects Azure DevOps YAML pipelines with template references:

### Main Pipeline File (`azure-pipelines.yml`)
```yaml
trigger:
  branches:
    include:
    - main

variables:
  buildConfiguration: 'Release'
  vmImageName: 'ubuntu-latest'

stages:
- stage: Build
  displayName: 'Build Stage'
  jobs:
  - template: templates/build-template.yml
    parameters:
      vmImage: $(vmImageName)
      buildConfiguration: $(buildConfiguration)

- stage: Test
  displayName: 'Test Stage'
  dependsOn: Build
  jobs:
  - template: templates/test-template.yml
    parameters:
      vmImage: $(vmImageName)

- stage: Deploy_Dev
  displayName: 'Deploy to Development'
  dependsOn: Test
  jobs:
  - template: templates/deploy-template.yml
    parameters:
      environment: 'Development'
```

### Template Files
```
templates/
├── build-template.yml
├── test-template.yml
└── deploy-template.yml
```

## Output Structure

After conversion, the tool generates:

```
output-directory/
├── Jenkinsfile                           # Main pipeline orchestrator
├── azure-pipelines.delete.yml            # Renamed original file
└── templates/
    ├── build-template.groovy             # Converted build template
    ├── test-template.groovy              # Converted test template
    ├── deploy-template.groovy            # Converted deploy template
    ├── build-template.delete.yml         # Renamed original
    ├── test-template.delete.yml          # Renamed original
    └── deploy-template.delete.yml        # Renamed original
```

## Generated Jenkinsfile

The main `Jenkinsfile` uses environment variables for flexible template loading:

```groovy
pipeline {
    agent any

    stages {
        stage('Build Stage') {
            steps {
                script {
                    def stageScript = load "${env.APP_PATH}/templates/build-template.groovy"
                    stageScript()
                }
            }
        }
        stage('Test Stage') {
            steps {
                script {
                    def stageScript = load "${env.APP_PATH}/templates/test-template.groovy"
                    stageScript()
                }
            }
        }
        // Additional stages...
    }
}
```

## Generated Template Files

Each template is converted to a Groovy script with method stubs:

```groovy
def call() {
    def stepNo = 1

    logStepMessage("Checkout Source Code", stepNo)
    checkoutSourceCode()
    stepNo = stepNo + 1

    logStepMessage("Build Solution", stepNo)
    buildSolution()
    stepNo = stepNo + 1

    // Additional steps...
}

def logStepMessage(displayName, stepNo) {
    echo "[Step ${stepNo}] ${displayName}"
}

def checkoutSourceCode() {
    // TODO: Implement
    // Checkout Source Code
    // Method implementation goes here
}

def buildSolution() {
    // TODO: Implement
    // Build Solution
    // Method implementation goes here
}

return this
```

## Supported Azure DevOps Features

### Job Types
- **Regular Jobs**: Standard job definitions with steps
- **Template References**: `template: templates/build-template.yml`
- **Deployment Jobs**: Azure DevOps deployment jobs with strategies

### YAML Structures
- **Stages**: Multi-stage pipeline support
- **Jobs**: Multiple jobs per stage
- **Steps**: Task conversion to method stubs
- **Parameters**: Template parameter extraction
- **Variables**: Pipeline variable handling

## Command Line Options

| Option | Description | Required |
|--------|-------------|----------|
| `--yaml` | Path to the Azure DevOps YAML pipeline file | Yes |
| `--output` | Output directory for generated Groovy files | Yes |

## Example Conversion Flow

1. **Input**: Azure DevOps YAML with templates
2. **Processing**: 
   - Parse YAML structure
   - Extract stages, jobs, and steps
   - Generate Groovy equivalents
   - Preserve folder structure
3. **Output**: 
   - Jenkins pipeline with template loading
   - Groovy templates with method stubs
   - Original files renamed to `.delete.yml`

## Jenkins Environment Setup

Set the `APP_PATH` environment variable in Jenkins to point to your workspace:

```groovy
environment {
    APP_PATH = "${WORKSPACE}"
}
```

## Development

### Project Structure
```
AzureYamlToGroovy/
├── Models/
│   └── PipelineModels.cs          # Data models
├── Services/
│   ├── YamlParserService.cs       # YAML parsing logic
│   ├── GroovyGeneratorService.cs  # Groovy generation
│   └── PipelineConverter.cs       # Main orchestrator
├── Program.cs                     # CLI entry point
└── AzureYamlToGroovy.csproj      # Project file
```

### Dependencies
- **YamlDotNet**: YAML parsing
- **System.CommandLine**: CLI interface

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

This project is licensed under the MIT License.