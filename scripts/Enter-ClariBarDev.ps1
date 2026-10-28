$ErrorActionPreference = 'Stop'
$clariRoot = 'E:\haoyue\cache\claribar'
$env:CLARIBAR_ROOT = $clariRoot
$env:TEMP = "$clariRoot\temp"
$env:TMP = $env:TEMP
$env:PATHEXT = '.COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC'
$env:DOTNET_ROOT = "$clariRoot\programs\dotnet"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = "$clariRoot\caches\dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = "$clariRoot\caches\nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = "$clariRoot\caches\nuget\http"
$env:NUGET_PLUGINS_CACHE_PATH = "$clariRoot\caches\nuget\plugins"
$env:NUGET_SCRATCH = "$clariRoot\caches\nuget\scratch"
$env:XDG_CACHE_HOME = "$clariRoot\caches\xdg"
$env:VCPKG_DEFAULT_BINARY_CACHE = "$clariRoot\caches\vcpkg\binary"
$env:VCPKG_DOWNLOADS = "$clariRoot\caches\vcpkg\downloads"
$env:_NT_SYMBOL_PATH = "srv*$clariRoot\caches\symbols*https://msdl.microsoft.com/download/symbols"
$env:GIT_CONFIG_GLOBAL = "$clariRoot\caches\git\config"
foreach ($directory in @($env:TEMP,$env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:NUGET_HTTP_CACHE_PATH,$env:NUGET_PLUGINS_CACHE_PATH,$env:NUGET_SCRATCH,$env:XDG_CACHE_HOME,$env:VCPKG_DEFAULT_BINARY_CACHE,$env:VCPKG_DOWNLOADS,(Split-Path $env:GIT_CONFIG_GLOBAL))) {
    [void](New-Item -ItemType Directory -Path $directory -Force)
}
$devCmd = "$clariRoot\programs\VisualStudio\Common7\Tools\VsDevCmd.bat"
if (Test-Path -LiteralPath $devCmd) {
    $devEnvironment = & "$env:SystemRoot\System32\cmd.exe" /d /s /c "`"$devCmd`" -no_logo -arch=x64 -host_arch=x64 >nul && set"
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the Visual Studio developer environment' }
    foreach ($line in $devEnvironment) {
        if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process') }
    }
}
$cmake = Get-ChildItem "$clariRoot\programs\cmake" -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName 'bin\cmake.exe') } | Select-Object -First 1
$toolPaths = @("$clariRoot\programs\Git\cmd", "$clariRoot\programs\dotnet", "$clariRoot\programs\ninja")
if ($cmake) { $toolPaths += (Join-Path $cmake.FullName 'bin') }
$env:PATH = ($toolPaths + @($env:PATH -split ';') | Select-Object -Unique) -join ';'
# Local download settings for this developer shell; no system proxy change.
$env:HTTP_PROXY = 'http://127.0.0.1:7897'
$env:HTTPS_PROXY = $env:HTTP_PROXY
$env:NO_PROXY = 'localhost,127.0.0.1,::1'
