# ============================================================
#  图标构建脚本：官方 Harness UI 黑色鲸鱼 SVG -> 多尺寸 app.ico
#  用法: powershell -ExecutionPolicy Bypass -File tools\build-icon.ps1
#  素材来源: https://github.com/deepseek-ai/deepseek-harness/blob/master/apps/web/public/favicon.svg
#            （与运行中 Harness UI 的 /favicon.svg 字节完全一致）
#  渲染: 官方矢量轮廓原样渲染为纯黑 #000000（SVG 默认填充色），不改绘
# ============================================================
param(
    [string]$Source = 'assets-src\deepseek-harness-official-favicon.svg',
    [string]$Output = 'src\DeepSeekHarnessDesktop\Assets\app.ico'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Add-Type -Path (Join-Path $PSScriptRoot 'SvgWhaleIconBuilder.cs') -ReferencedAssemblies 'PresentationCore', 'WindowsBase', 'System.Xaml'

$src = Join-Path $root $Source
$out = Join-Path $root $Output
if (-not (Test-Path $src)) { throw "找不到素材文件: $src" }

$svgText = Get-Content $src -Raw
$m = [regex]::Match($svgText, '<path\b[^>]*\sd="([^"]+)"')
if (-not $m.Success) { throw 'SVG 中未找到 path 数据' }
$pathData = $m.Groups[1].Value

[SvgWhaleIconBuilder]::BuildIco($pathData, $out)
Write-Host "已生成: $out" -ForegroundColor Green
