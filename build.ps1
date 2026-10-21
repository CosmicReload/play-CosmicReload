# Builds agent\build\cosmic-agent.jar and dist\Play CosmicReload.exe (the agent is packed inside the exe).
# Needs a JDK (8 or newer) and an ASM 9 jar (ASM_JAR, or one from the Gradle cache). The exe is compiled with the
# C# compiler that ships with Windows (.NET Framework 4), so nothing else is needed.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# agent
$jdk = $env:COSMIC_JAVA8
if (-not $jdk) { $jdk = 'C:\Program Files\Eclipse Adoptium\jdk-8.0.462.8-hotspot' }
$asm = $env:ASM_JAR
if (-not $asm) {
    $asm = Get-ChildItem "$env:USERPROFILE\.gradle" -Recurse -Filter 'asm-9.*.jar' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike '*sources*' } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $asm) { throw 'ASM 9 jar not found (set ASM_JAR).' }
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
$version = [regex]::Match((Get-Content $source -Raw), 'Version = "([0-9.]+)"').Groups[1].Value
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
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
& $makensis /V2 "/DVERSION=$version" "/DAPP_EXE=$exe" (Join-Path $installer 'installer.nsi')
if ($LASTEXITCODE -ne 0) { throw 'makensis failed' }
$setup = Join-Path $root "dist\PlayCosmicReloadSetup-$version.exe"
Write-Host "Built $setup"

# publish into the website (when this sits next to it): the installer under a fixed name, plus the manifest the
# app's updater reads. Redeploy the website to release it.
$site = Join-Path $root '..\website\download'
if (Test-Path $site) {
    Copy-Item $setup (Join-Path $site 'PlayCosmicReloadSetup.exe') -Force
    $sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = [math]::Round((Get-Item $setup).Length / 1KB)
    $manifest = '{"version": "' + $version + '", "url": "https://cosmicreload.com/download/PlayCosmicReloadSetup.exe", "sha256": "' + $sha + '", "sizeKB": ' + $size + '}'
    [IO.File]::WriteAllText((Join-Path $site 'play-cosmicreload.json'), $manifest + "`n")
    Write-Host "Published $version to website\download (redeploy the site to release it)"
}
