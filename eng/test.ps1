param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$Model = "Qwen/Qwen3.6-27B:coding",
    [string]$PayloadPath = "artifacts/smoke-tests/test_payload.json"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedPayloadPath = if ([System.IO.Path]::IsPathRooted($PayloadPath)) {
    $PayloadPath
}
else {
    Join-Path $repoRoot $PayloadPath
}

$payloadDirectory = Split-Path -Parent $resolvedPayloadPath
if (-not [string]::IsNullOrWhiteSpace($payloadDirectory)) {
    New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null
}

$json = @"
{
  "model": "$Model",
  "messages": [
    {"role": "system", "content": "You are a helpful assistant. Please think step by step."},
    {"role": "user", "content": "Write a short 3-sentence story about a brave compiler."}
  ],
  "stream": true
}
"@

$json | Out-File -FilePath $resolvedPayloadPath -Encoding utf8

$uri = "$($BaseUrl.TrimEnd('/'))/v1/chat/completions"
Write-Host "Sending streaming chat completions request to $uri..." -ForegroundColor Cyan
curl.exe -X POST "$uri" -H "Content-Type: application/json" -d "@$resolvedPayloadPath" -N