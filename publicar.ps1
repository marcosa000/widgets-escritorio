# Compila en Release y copia el resultado a .\publicado (widgets) y .\publicado\gatito (Simón),
# que es lo que arranca con Windows.
# (No se usa "dotnet publish": en apps WinUI sin empaquetar deja afuera las vistas compiladas .xbf y Widgets.pri.)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
Get-Process Widgets, Gatito -ErrorAction SilentlyContinue | Stop-Process   # los exe abiertos bloquean la copia
dotnet build Widgets -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }
dotnet build Gatito -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }

# robocopy: menos de 8 es éxito. /XD gatito: el espejo de los widgets no borra la carpeta del gato.
robocopy "Widgets\bin\Release\net10.0-windows10.0.19041.0\win-x64" "publicado" /MIR /XD gatito /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { exit 1 }
robocopy "Gatito\bin\Release\net10.0-windows" "publicado\gatito" /MIR /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { exit 1 }

# Arranque con Windows (solo este usuario). Se reescribe siempre: si el repo cambia de carpeta, queda bien.
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$widgets = Join-Path $PSScriptRoot "publicado\Widgets.exe"
$gatito = Join-Path $PSScriptRoot "publicado\gatito\Gatito.exe"
Set-ItemProperty $run -Name "Widgets de escritorio" -Value "`"$widgets`""
Set-ItemProperty $run -Name "Gatito Simon" -Value "`"$gatito`""
Start-Process $widgets
Start-Process $gatito
exit 0
