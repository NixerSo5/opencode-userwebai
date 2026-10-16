<#
.SYNOPSIS
    Install the opencode provider config for this project.
    将本项目的 opencode provider 配置写入 opencode 配置文件。

.DESCRIPTION
    Reads the "provider" fragment from config\webai.opencode.json and merges it
    into the "provider" node of the target opencode config file. Existing settings
    (mcp, plugin, instructions, other providers, ...) are preserved. A timestamped
    backup is created before writing.

    读取 config\webai.opencode.json 中的 "provider" 片段，合并进目标 opencode.jsonc
    的 "provider" 节点；其它已有配置（mcp、plugin、instructions、其它 provider 等）
    保持不变。写入前会自动生成带时间戳的备份。

    NOTE / 注意:
    The target file is re-serialized, so comments and custom formatting are lost.
    If parsing fails (e.g. the file contains comments or trailing commas), the
    script aborts and asks you to merge manually.
    目标文件会被重新序列化，原有注释与排版会丢失。若解析失败（例如含注释或尾逗号），
    脚本会中止并提示手动合并。

.PARAMETER Config
    Provider config fragment. Default: config\webai.opencode.json

.PARAMETER Target
    opencode config file. Default: %USERPROFILE%\.config\opencode\opencode.jsonc

.EXAMPLE
    pwsh -File .\install-webai.ps1

.EXAMPLE
    pwsh -File .\install-webai.ps1 -Target "$env:USERPROFILE\.config\opencode\opencode.json"

.EXAMPLE
    pwsh -File .\install-webai.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $Config = (Join-Path $PSScriptRoot 'config\webai.opencode.json'),
    [string] $Target = (Join-Path $env:USERPROFILE '.config\opencode\opencode.jsonc')
)

$ErrorActionPreference = 'Stop'

function Read-JsonFile {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File not found / 文件不存在：$Path"
    }
    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [ordered]@{}
    }
    try {
        return ($raw | ConvertFrom-Json -AsHashtable)
    }
    catch {
        throw "Failed to parse JSON / 无法解析 JSON：$Path`n$($_.Exception.Message)`n" +
              "(If the file has comments or trailing commas, merge manually. / 若含注释或尾逗号，请手动合并。)"
    }
}

if (-not (Test-Path -LiteralPath $Config)) {
    throw "Config fragment not found / 找不到配置文件：$Config"
}

$fragment = Read-JsonFile -Path $Config
if (-not $fragment.Contains('provider') -or $fragment['provider'].Count -eq 0) {
    throw "No 'provider' node in fragment / 配置片段里没有 provider 节点：$Config"
}

if (-not (Test-Path -LiteralPath $Target)) {
    Write-Host "Target does not exist, will create / 目标文件不存在，将新建：$Target"
    $targetDir = Split-Path -Parent $Target
    if ($targetDir -and -not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }
    $doc = [ordered]@{
        '$schema'  = 'https://opencode.ai/config.json'
        'provider' = [ordered]@{}
    }
}
else {
    $doc = Read-JsonFile -Path $Target
}

if (-not $doc.Contains('provider') -or $null -eq $doc['provider']) {
    $doc['provider'] = [ordered]@{}
}

foreach ($key in $fragment['provider'].Keys) {
    $action = if ($doc['provider'].Contains($key)) { 'update / 更新 provider' } else { 'add / 新增 provider' }
    Write-Host ("{0}: {1}" -f $action, $key)
    $doc['provider'][$key] = $fragment['provider'][$key]
}

$json = $doc | ConvertTo-Json -Depth 100

if ($PSCmdlet.ShouldProcess($Target, 'Write opencode config / 写入 opencode 配置')) {
    if (Test-Path -LiteralPath $Target) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backup = "$Target.bak-$stamp"
        Copy-Item -LiteralPath $Target -Destination $backup -Force
        Write-Host "Backup / 已备份：$backup"
    }

    Set-Content -LiteralPath $Target -Value $json -Encoding UTF8
    Write-Host "Written / 已写入：$Target"
    Write-Host ''
    Write-Host 'Available models / 可用模型：'
    foreach ($key in $fragment['provider'].Keys) {
        foreach ($model in $fragment['provider'][$key]['models'].Keys) {
            Write-Host ("  {0}/{1}" -f $key, $model)
        }
    }
    Write-Host ''
    Write-Host 'Restart opencode completely for /models to pick up the new models.'
    Write-Host '修改后需要完全重启 opencode，/models 里才会出现新模型。'
}
