param([string]$OutputDirectory, [switch]$Restore)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$root = Join-Path $env:LOCALAPPDATA 'Ferrum'
$state = Join-Path $root 'DebugDeployment'
$destination = Join-Path $root 'Debug\Revit2024'
$addins = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
$registration = Join-Path $addins 'FerrumAddinDev.addin'
$updater = Join-Path $root 'Updater\Ferrum.Updater.exe'
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close all Revit processes before deploying or restoring Ferrum.' }
if (-not $Restore) {
    if (-not $OutputDirectory) { throw 'OutputDirectory is required.' }
    $output = (Resolve-Path -LiteralPath $OutputDirectory).Path
    if (-not (Test-Path -LiteralPath (Join-Path $output 'FerrumAddinDev.dll'))) { throw 'Debug DLL was not built.' }
}
if ((Test-Path -LiteralPath $updater) -and (Get-Process -Name 'Ferrum.Updater' -ErrorAction SilentlyContinue)) {
    & $updater --silent --shutdown
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop the updater.' }
}
if ($Restore) {
    if (-not (Test-Path -LiteralPath (Join-Path $state 'ready'))) { throw 'No saved release registration.' }
    if (Test-Path -LiteralPath (Join-Path $state 'registration.addin')) {
        Copy-Item -LiteralPath (Join-Path $state 'registration.addin') -Destination $registration -Force
    } elseif (Test-Path -LiteralPath $registration) { Remove-Item -LiteralPath $registration }
    foreach ($name in @('pending.json', 'updates-paused')) {
        $saved = Join-Path $state $name
        $live = Join-Path $root $name
        if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $live -Force }
        elseif (Test-Path -LiteralPath $live) { Remove-Item -LiteralPath $live }
    }
    # Only the fixed state files belonging to this script are removed.
    foreach ($name in @('ready', 'registration.addin', 'pending.json', 'updates-paused')) {
        $saved = Join-Path $state $name
        if (Test-Path -LiteralPath $saved) { Remove-Item -LiteralPath $saved }
    }
    if (Test-Path -LiteralPath $updater) { Start-Process -FilePath $updater -ArgumentList '--tray' -WindowStyle Hidden }
    Write-Output 'Ferrum release registration and update settings restored.'
    return
}
New-Item -ItemType Directory -Force -Path $state, $destination, $addins | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $state 'ready'))) {
    if (Test-Path -LiteralPath $registration) { Copy-Item -LiteralPath $registration -Destination (Join-Path $state 'registration.addin') }
    foreach ($name in @('pending.json', 'updates-paused')) {
        $live = Join-Path $root $name
        if (Test-Path -LiteralPath $live) { Copy-Item -LiteralPath $live -Destination (Join-Path $state $name) }
    }
    [IO.File]::WriteAllText((Join-Path $state 'ready'), 'Debug deployment backup')
}
[IO.File]::WriteAllText((Join-Path $root 'updates-paused'), 'Ferrum Debug deployment')
$pending = Join-Path $root 'pending.json'
if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending }
# Additional DLL resources first; the current build always wins over stale DLL copies.
foreach ($source in @((Join-Path $repository 'DLL'), $output)) {
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
        if ($file.Name -match '^(RevitAPI(UI)?|AdWindows)\.dll$|^Updater\.' -or $file.Extension -in @('.pem', '.pfx', '.cs')) { continue }
        $relative = $file.FullName.Substring($source.TrimEnd('\').Length).TrimStart('\')
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
$document = New-Object Xml.XmlDocument
$document.Load((Join-Path $repository 'FerrumAddinDev.addin'))
foreach ($assembly in $document.SelectNodes('/RevitAddIns/AddIn/Assembly')) { $assembly.InnerText = Join-Path $destination 'FerrumAddinDev.dll' }
$document.Save($registration)
Write-Output ('Ferrum Debug deployed (DLL, PDB, dependencies, resources): ' + $destination)
