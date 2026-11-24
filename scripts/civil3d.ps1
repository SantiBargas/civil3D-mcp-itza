# Opens, closes and rebuilds Civil 3D 2026 for the development loop, without manual steps.
#   .\scripts\civil3d.ps1 -Abrir [-Dibujo <ruta.dwg>]   new metric drawing, or the given .dwg
#   .\scripts\civil3d.ps1 -Cerrar                       kills acad.exe (unsaved changes are lost)
#   .\scripts\civil3d.ps1 -Compilar [-Ts] [-Abrir]      close, build plugin, refresh autoload bundle
# The plugin loads by itself from the CivilMcpItza.bundle autoload bundle; -Compilar copies the
# fresh build there, so NETLOAD is never needed.
param(
  [switch]$Abrir,
  [switch]$Cerrar,
  [switch]$Compilar,
  [switch]$Ts,
  [string]$Dibujo,
  [int]$TimeoutSegundos = 240
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$acadExe = "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe"
$aecBase = "C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx"
$plantillaMetrica = Join-Path $env:LOCALAPPDATA "Autodesk\C3D 2026\esp\Template\_Autodesk Civil 3D (Metric) NCS.dwt"
$bundleContents = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\CivilMcpItza.bundle\Contents\2026"
$buildDir = Join-Path $repoRoot "Civil3D-MCP-Plugin\bin\Release\net8.0-windows"
$pluginFiles = @("Civil3DMcpPlugin.dll", "Civil3DMcpPlugin.deps.json", "Civil3DMcpPlugin.runtimeconfig.json")

function Close-Civil3D {
  $procs = @(Get-Process acad -ErrorAction SilentlyContinue)
  if ($procs.Count -eq 0) { Write-Output "Civil 3D ya estaba cerrado."; return }
  # Polite close first (fast when the drawing is saved). Killing acad.exe took 1–4+ min to finish
  # (crash-report dump), so the forced kill is only the fallback for a hung or prompting Civil 3D.
  foreach ($p in $procs) { [void]$p.CloseMainWindow() }
  $procs | Wait-Process -Timeout 45 -ErrorAction SilentlyContinue
  $vivos = @(Get-Process acad -ErrorAction SilentlyContinue)
  if ($vivos.Count -gt 0) {
    Write-Output "No cerró por las buenas (¿colgado o con un diálogo?): se fuerza."
    $vivos | Stop-Process -Force
    $vivos | Wait-Process -Timeout 600 -ErrorAction SilentlyContinue
  }
  if (Get-Process acad -ErrorAction SilentlyContinue) { throw "acad.exe sigue vivo después de forzar el cierre." }
  Write-Output "Civil 3D cerrado ($($procs.Count) proceso/s)."
}

function Test-PluginPort {
  $client = New-Object System.Net.Sockets.TcpClient
  try {
    $async = $client.BeginConnect("127.0.0.1", 8080, $null, $null)
    return ($async.AsyncWaitHandle.WaitOne(500) -and $client.Connected)
  } catch { return $false } finally { $client.Close() }
}

function Open-Civil3D {
  if (Get-Process acad -ErrorAction SilentlyContinue) { throw "Civil 3D ya está abierto; usar -Cerrar antes." }
  $argList = @("/ld", "`"$aecBase`"", "/p", "`"<<C3D_Metric>>`"", "/product", "C3D", "/language", "es-ES")
  if ($Dibujo) {
    $ruta = (Resolve-Path $Dibujo).Path
    $argList = @("`"$ruta`"") + $argList
  } else {
    $argList += @("/t", "`"$plantillaMetrica`"")
  }
  Start-Process -FilePath $acadExe -ArgumentList $argList | Out-Null

  # Ready = plugin listening on 8080 and a drawing (not the Start tab) in the window title.
  $limite = (Get-Date).AddSeconds($TimeoutSegundos)
  while ((Get-Date) -lt $limite) {
    Start-Sleep -Seconds 3
    $acad = Get-Process acad -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($acad -and $acad.MainWindowTitle -match '\[(.+\.dwg)\]' -and (Test-PluginPort)) {
      Write-Output "Civil 3D listo: $($Matches[1]), plugin escuchando en 8080."
      return
    }
  }
  throw "Civil 3D no quedó listo en $TimeoutSegundos s (¿diálogo abierto o plugin sin cargar?)."
}

# Without this, every rebuilt (unsigned) DLL triggers the "Archivo ejecutable no firmado" dialog.
# Written with acad.exe closed: AutoCAD rewrites the profile variables on exit.
function Set-TrustedBundlePath {
  $profiles = "HKCU:\Software\Autodesk\AutoCAD\R25.1\ACAD-9100:40A\Profiles"
  foreach ($perfil in @("<<C3D_Metric>>", "<<C3D_Imperial>>")) {
    $vars = Join-Path $profiles "$perfil\Variables"
    if (-not (Test-Path -LiteralPath $vars)) { continue }
    $actual = (Get-ItemProperty -LiteralPath $vars).TRUSTEDPATHS
    if ($actual -and $actual.Split(";") -contains $bundleContents) { continue }
    $nuevo = if ($actual) { $actual.TrimEnd(";") + ";" + $bundleContents } else { $bundleContents }
    Set-ItemProperty -LiteralPath $vars -Name TRUSTEDPATHS -Value $nuevo
    Write-Output "Bundle agregado a TRUSTEDPATHS de $perfil."
  }
}

function Build-Plugin {
  Close-Civil3D
  Set-TrustedBundlePath
  dotnet build (Join-Path $repoRoot "Civil3D-MCP-Plugin\Civil3DMcpPlugin.csproj") -c Release --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "Falló la compilación del plugin." }
  foreach ($f in $pluginFiles) { Copy-Item (Join-Path $buildDir $f) $bundleContents -Force }
  Write-Output "Plugin compilado y copiado al bundle de autocarga."
  if ($Ts) {
    Push-Location $repoRoot
    try { npm run build; if ($LASTEXITCODE -ne 0) { throw "Falló npm run build." } } finally { Pop-Location }
    Write-Output "Servidor TS compilado: falta /mcp -> Reconnect en Claude Code."
  }
}

if (-not ($Abrir -or $Cerrar -or $Compilar)) { throw "Indicar -Abrir, -Cerrar o -Compilar." }
if ($Cerrar) { Close-Civil3D }
if ($Compilar) { Build-Plugin }
if ($Abrir) { Open-Civil3D }
