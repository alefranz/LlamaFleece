$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'LlamaFleece\LlamaFleece.csproj'

dotnet run -c release --project $projectPath