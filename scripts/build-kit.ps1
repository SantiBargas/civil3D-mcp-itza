# Builds the install kit for a non-developer machine: a zip with the Civil 3D
# autoload bundle (prebuilt plugin, no Autodesk DLLs) and the Claude Desktop .mcpb.
# The plugin is built straight into the kit folder, so Civil 3D can stay open.
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $repoRoot "package.json") -Raw | ConvertFrom-Json).version
$kitName = "civil3D-mcp-itza-kit-$version"
$kitRoot = Join-Path $repoRoot "dist\kit"
$kitDir = Join-Path $kitRoot $kitName
$bundleDir = Join-Path $kitDir "CivilMcpItza.bundle"
$contentsDir = Join-Path $bundleDir "Contents"
$pluginBuildDir = Join-Path $kitRoot "plugin-build"
$zipPath = Join-Path $kitRoot "$kitName.zip"

if (Test-Path $kitRoot) {
  Remove-Item $kitRoot -Recurse -Force
}
New-Item -ItemType Directory -Force $contentsDir | Out-Null

& dotnet build (Join-Path $repoRoot "Civil3D-MCP-Plugin\Civil3DMcpPlugin.csproj") -c Release -o $pluginBuildDir
if ($LASTEXITCODE -ne 0) {
  throw "dotnet build failed."
}
foreach ($file in @("Civil3DMcpPlugin.dll", "Civil3DMcpPlugin.deps.json", "Civil3DMcpPlugin.runtimeconfig.json")) {
  Copy-Item (Join-Path $pluginBuildDir $file) $contentsDir
}
$autodeskDlls = Get-ChildItem $contentsDir -Filter "*.dll" | Where-Object { $_.Name -ne "Civil3DMcpPlugin.dll" }
if ($autodeskDlls) {
  throw "Unexpected DLLs in the bundle (Autodesk assemblies must not be redistributed): $($autodeskDlls.Name -join ', ')"
}

$manifestTemplate = Get-Content (Join-Path $repoRoot "packaging\civil3d-bundle\PackageContents.xml") -Raw
$manifest = $manifestTemplate.Replace("__VERSION__", $version)
[System.IO.File]::WriteAllText((Join-Path $bundleDir "PackageContents.xml"), $manifest, (New-Object System.Text.UTF8Encoding $false))

Push-Location $repoRoot
try {
  & npm.cmd run build
  if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
  & npm.cmd run package:claude
  if ($LASTEXITCODE -ne 0) { throw "npm run package:claude failed." }
} finally {
  Pop-Location
}
Copy-Item (Join-Path $repoRoot "dist\claude-desktop\civil3d-mcp-itza-$version.mcpb") $kitDir

Remove-Item $pluginBuildDir -Recurse -Force
Compress-Archive -Path $kitDir -DestinationPath $zipPath

Write-Host ""
Write-Host "Kit: $zipPath"
