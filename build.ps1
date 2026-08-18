# ============================================================
#  DeepSeek Harness Desktop 一键构建脚本
#  用法: powershell -ExecutionPolicy Bypass -File build.ps1
#  可选参数: -SkipTests 跳过测试; -SkipPublish 跳过发布
# ============================================================
param(
    [switch]$SkipTests,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

# NuGet 缓存与 .NET CLI 状态保持在项目目录内，避免污染用户目录
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:DOTNET_CLI_HOME  = Join-Path $root '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Write-Host '==> Restore' -ForegroundColor Cyan
dotnet restore .\DeepSeekHarnessDesktop.sln
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore 失败' }

Write-Host '==> Build (Release)' -ForegroundColor Cyan
dotnet build .\DeepSeekHarnessDesktop.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败' }

if (-not $SkipTests) {
    Write-Host '==> Test' -ForegroundColor Cyan
    dotnet test .\tests\DeepSeekHarnessDesktop.Tests\DeepSeekHarnessDesktop.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test 失败' }
}

if (-not $SkipPublish) {
    Write-Host '==> Publish (win-x64, self-contained)' -ForegroundColor Cyan
    dotnet publish .\src\DeepSeekHarnessDesktop\DeepSeekHarnessDesktop.csproj -c Release -r win-x64 `
        --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o .\publish
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }
}

Write-Host ''
Write-Host '构建完成。' -ForegroundColor Green
Write-Host "成品目录: $root\publish"
Write-Host '主程序:   publish\DeepSeek Harness Desktop.exe'
