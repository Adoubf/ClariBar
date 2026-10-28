$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot
$localConfig = Join-Path $sourceRoot '.claribar.local.ps1'
if (Test-Path -LiteralPath $localConfig) { . $localConfig }
$clariRoot = if ($env:CLARIBAR_ROOT) { [IO.Path]::GetFullPath($env:CLARIBAR_ROOT) } else { Join-Path $sourceRoot '.build' }
$env:CLARIBAR_ROOT = $clariRoot
$env:TEMP = Join-Path $clariRoot 'temp'
$env:TMP = $env:TEMP
if ($env:PATHEXT -notmatch '(?i)\.EXE') { $env:PATHEXT = '.COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC' }
$env:DOTNET_CLI_HOME = Join-Path $clariRoot 'caches\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $clariRoot 'caches\nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $clariRoot 'caches\nuget\http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $clariRoot 'caches\nuget\plugins'
$env:NUGET_SCRATCH = Join-Path $clariRoot 'caches\nuget\scratch'
$env:XDG_CACHE_HOME = Join-Path $clariRoot 'caches\xdg'
$env:VCPKG_DEFAULT_BINARY_CACHE = Join-Path $clariRoot 'caches\vcpkg\binary'
$env:VCPKG_DOWNLOADS = Join-Path $clariRoot 'caches\vcpkg\downloads'
$directories = @($env:TEMP,$env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:NUGET_HTTP_CACHE_PATH,$env:NUGET_PLUGINS_CACHE_PATH,$env:NUGET_SCRATCH,$env:XDG_CACHE_HOME,$env:VCPKG_DEFAULT_BINARY_CACHE,$env:VCPKG_DOWNLOADS,"$clariRoot\logs","$clariRoot\artifacts\ClariBar")
foreach ($directory in $directories) { [void](New-Item -ItemType Directory -Path $directory -Force) }
$portableDotnet = Join-Path $clariRoot 'programs\dotnet'
if (Test-Path (Join-Path $portableDotnet 'dotnet.exe')) {
    $env:DOTNET_ROOT = $portableDotnet
    $env:DOTNET_ROOT_X64 = $portableDotnet
    $env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR = $portableDotnet
}
$devCmd = Join-Path $clariRoot 'programs\VisualStudio\Common7\Tools\VsDevCmd.bat'
if (!(Test-Path -LiteralPath $devCmd) -and !$env:VSCMD_VER) {
    $vswhere = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $vsPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($vsPath) { $devCmd = Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat' }
    }
}
if (Test-Path -LiteralPath $devCmd) {
    $devEnvironment = & "$env:SystemRoot\System32\cmd.exe" /d /s /c ('"' + $devCmd + '" -no_logo -arch=x64 -host_arch=x64 >nul && set')
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the Visual Studio developer environment.' }
    foreach ($line in $devEnvironment) {
        if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process') }
    }
}
$toolPaths = @(@("$clariRoot\programs\Git\cmd","$clariRoot\programs\dotnet","$clariRoot\programs\ninja") | Where-Object { Test-Path -LiteralPath $_ })
$cmake = Get-ChildItem "$clariRoot\programs\cmake" -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName 'bin\cmake.exe') } | Select-Object -First 1
if ($cmake) { $toolPaths += (Join-Path $cmake.FullName 'bin') }
$env:PATH = (@($toolPaths) + @($env:PATH -split ';') | Select-Object -Unique) -join ';'
if ($env:CLARIBAR_PROXY) {
    $env:HTTP_PROXY = $env:CLARIBAR_PROXY
    $env:HTTPS_PROXY = $env:CLARIBAR_PROXY
}
