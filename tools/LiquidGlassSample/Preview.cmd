@echo off
dotnet run --project "%~dp0LiquidGlassSample.csproj" --configuration Release
if errorlevel 1 pause
