$ErrorActionPreference = 'Stop'

$project = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot 'bin\SolarFlareShield'
$refsDir = Join-Path $project '.tools\nuget\refs\build\.NETFramework\v4.8'
$compiler = Join-Path $project '.tools\nuget\compilers\tasks\netcore\bincore\csc.dll'
$dotnet = Join-Path $project '.rimsearcher\dotnet\dotnet.exe'
$rimWorld = 'D:\Steam\steamapps\common\RimWorld'
$harmony = 'D:\Steam\steamapps\workshop\content\294100\2009463077\Current\Assemblies\0Harmony.dll'

$defsOut = Join-Path $out 'Defs'
$texturesOut = Join-Path $out 'Textures'
New-Item -ItemType Directory -Force -Path (Join-Path $out 'Assemblies'), (Join-Path $out 'About') | Out-Null
Remove-Item -LiteralPath $defsOut -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $defsOut | Out-Null
Remove-Item -LiteralPath $texturesOut -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $texturesOut | Out-Null

$references = Get-ChildItem $refsDir -File -Filter '*.dll' |
    Where-Object { $_.Name -notin @('System.EnterpriseServices.Wrapper.dll', 'System.EnterpriseServices.Thunk.dll') } |
    ForEach-Object { $_.FullName }
$references += @(
    (Join-Path $rimWorld 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'),
    (Join-Path $rimWorld 'RimWorldWin64_Data\Managed\UnityEngine.CoreModule.dll'),
    (Join-Path $rimWorld 'RimWorldWin64_Data\Managed\netstandard.dll'),
    $harmony
)

$arguments = @(
    $compiler,
    '-noconfig', '-nostdlib+', '-target:library', '-langversion:latest',
    '-nullable:enable', '-optimize+', '-debug:portable',
    ('-out:' + (Join-Path $out 'Assemblies\SolarFlareShield.dll'))
)
$arguments += $references | ForEach-Object { '-r:' + $_ }
$arguments += Get-ChildItem (Join-Path $PSScriptRoot 'Source') -File -Filter '*.cs' | ForEach-Object { $_.FullName }

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item (Join-Path $PSScriptRoot 'About\About.xml') (Join-Path $out 'About\About.xml') -Force
Copy-Item (Join-Path $PSScriptRoot 'Defs\*') $defsOut -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Textures\*') $texturesOut -Recurse -Force
Write-Host "Built $out"
