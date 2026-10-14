# Compila en Release y copia el resultado a .\publicado, que es lo que arranca con Windows.
# (No se usa "dotnet publish": en apps WinUI sin empaquetar deja afuera las vistas compiladas .xbf y Widgets.pri.)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
Get-Process Widgets -ErrorAction SilentlyContinue | Stop-Process   # el exe abierto bloquea la copia
dotnet build Widgets -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }
robocopy "Widgets\bin\Release\net10.0-windows10.0.19041.0\win-x64" "publicado" /MIR /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { exit 1 }   # robocopy: menos de 8 es éxito
# Arranque con Windows (solo este usuario). Se reescribe siempre: si el repo cambia de carpeta, queda bien.
$exe = Join-Path $PSScriptRoot "publicado\Widgets.exe"
Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Widgets de escritorio" -Value "`"$exe`""
Start-Process $exe
exit 0
