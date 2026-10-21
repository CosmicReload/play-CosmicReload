# Builds agent\build\cosmic-agent.jar, build\Play CosmicReload.exe (the agent is packed inside the exe) and
# dist\PlayCosmicReloadSetup.exe + dist\play-cosmicreload.json (the manifest the app's updater reads).
# Needs a JDK 8 (COSMIC_JAVA8, the usual Adoptium path, or JAVA_HOME) and NSIS 3. ASM is downloaded from Maven Central and checked
# against a pinned SHA-256. The exe is compiled with the C# compiler that ships with Windows (.NET Framework 4).
# Releases are built by GitHub Actions (.github/workflows/release.yml) from a version tag; this script is what it runs.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# agent
$jdk = $env:COSMIC_JAVA8
if (-not $jdk -and (Test-Path 'C:\Program Files\Eclipse Adoptium\jdk-8.0.462.8-hotspot')) { $jdk = 'C:\Program Files\Eclipse Adoptium\jdk-8.0.462.8-hotspot' }
if (-not $jdk) { $jdk = $env:JAVA_HOME }
$asm = $env:ASM_JAR
if (-not $asm) {
    $asm = Join-Path $root 'agent\.cache\asm-9.7.jar'
    $asmSha = 'adf46d5e34940bdf148ecdd26a9ee8eea94496a72034ff7141066b3eea5c4e9d'
    if (-not (Test-Path $asm) -or (Get-FileHash $asm -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asmSha) {
        New-Item -ItemType Directory -Force (Split-Path $asm) | Out-Null
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest 'https://repo1.maven.org/maven2/org/ow2/asm/asm/9.7/asm-9.7.jar' -OutFile $asm -UseBasicParsing
        if ((Get-FileHash $asm -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asmSha) { throw 'ASM download failed its SHA-256 check' }
    }
}
$agent = Join-Path $root 'agent'
$classes = Join-Path $agent 'build\classes'
$jar = Join-Path $agent 'build\cosmic-agent.jar'
Remove-Item (Join-Path $agent 'build') -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $classes | Out-Null
$sources = Get-ChildItem (Join-Path $agent 'src') -Recurse -Filter *.java | ForEach-Object FullName
& "$jdk\bin\javac.exe" -source 1.8 -target 1.8 -nowarn -cp $asm -d $classes $sources
if ($LASTEXITCODE -ne 0) { throw 'javac failed' }
Push-Location $classes
try { & "$jdk\bin\jar.exe" xf $asm org } finally { Pop-Location }
Copy-Item (Join-Path $root 'LICENSE-ASM.txt') $classes
& "$jdk\bin\jar.exe" cfm $jar (Join-Path $agent 'manifest.txt') -C $classes .
if ($LASTEXITCODE -ne 0) { throw 'jar failed' }
Write-Host "Built $jar"

# app
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force $build | Out-Null
$exe = Join-Path $build 'Play CosmicReload.exe'
$app = Join-Path $root 'app'
$source = Join-Path $app 'PlayCosmicReload.cs'
& $csc /nologo /target:winexe /optimize+ "/out:$exe" "/win32icon:$app\icon.ico" `
    "/resource:$jar,cosmic-agent.jar" "/resource:$app\logo.png,logo.png" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll $source
if ($LASTEXITCODE -ne 0) { throw 'csc failed' }
Write-Host "Built $exe"

# installer (version comes from Config.Version in the app)
$version = [regex]::Match((Get-Content $source -Raw), '\bVersion = "([0-9.]+)"').Groups[1].Value
$minimum = [regex]::Match((Get-Content $source -Raw), 'MinimumVersion = "([0-9.]+)"').Groups[1].Value
$makensis = $null
if ($env:NSIS_HOME) { $makensis = Join-Path $env:NSIS_HOME 'makensis.exe' }
if (-not $makensis -or -not (Test-Path $makensis)) {
    $makensis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $makensis) {
    $makensis = Get-ChildItem "$env:LOCALAPPDATA\electron-builder\Cache" -Recurse -Filter makensis.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -notlike '*\Bin' } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $makensis) { throw 'NSIS not found (install NSIS 3 or set NSIS_HOME).' }
$installer = Join-Path $root 'installer'
if (-not (Test-Path (Join-Path $installer 'welcome.bmp'))) { & (Join-Path $installer 'make-art.ps1') }
$dist = Join-Path $root 'dist'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null
& $makensis /V2 "/DVERSION=$version" "/DAPP_EXE=$exe" (Join-Path $installer 'installer.nsi')
if ($LASTEXITCODE -ne 0) { throw 'makensis failed' }
$setup = Join-Path $dist 'PlayCosmicReloadSetup.exe'
Move-Item (Join-Path $dist "PlayCosmicReloadSetup-$version.exe") $setup
Write-Host "Built $setup"

# update manifest: points at this version's GitHub release asset (cosmicreload.com/download/play-cosmicreload.json
# redirects to the latest release's copy of this file)
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$size = [math]::Round((Get-Item $setup).Length / 1KB)
$url = "https://github.com/CosmicReload/play-CosmicReload/releases/download/v$version/PlayCosmicReloadSetup.exe"
$manifest = '{"version": "' + $version + '", "url": "' + $url + '", "minimum": "' + $minimum + '", "sha256": "' + $sha + '", "sizeKB": ' + $size + '}'
[IO.File]::WriteAllText((Join-Path $dist 'play-cosmicreload.json'), $manifest + "`n")
Write-Host "Version $version, SHA-256 $sha"
