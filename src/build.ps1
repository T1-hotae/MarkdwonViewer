# MdViewer 빌드: .NET Framework에 들어 있는 csc.exe로 컴파일한다 (별도 설치 필요 없음).
# 사용: powershell -ExecutionPolicy Bypass -File build.ps1
# 결과: ..\MdViewer.exe (+ DLL 3개), ..\dist\MdViewer-Setup.exe (다른 PC 설치용 파일 하나)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$lib = Join-Path $src 'lib'
$out = Split-Path $src -Parent
$obj = Join-Path $src 'obj'
$dist = Join-Path $out 'dist'
New-Item -ItemType Directory -Force $obj, $dist | Out-Null

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
function Compile([string[]]$a) {
    & $csc @a
    if ($LASTEXITCODE -ne 0) { throw "컴파일 실패" }
}

# 코드 하이라이트 색: 밝은 테마 + 다크 테마
$light = Get-Content -Raw -Encoding UTF8 (Join-Path $lib 'github.min.css')
$dark = Get-Content -Raw -Encoding UTF8 (Join-Path $lib 'github-dark.min.css')
[IO.File]::WriteAllText((Join-Path $obj 'highlight.css'), "$light`n@media (prefers-color-scheme: dark) { $dark }`n", [Text.UTF8Encoding]::new($false))

# WebView2 DLL은 exe 옆에 둔다
$dlls = 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll'
foreach ($d in $dlls) { Copy-Item (Join-Path $lib $d) $out -Force }

$exe = Join-Path $out 'MdViewer.exe'
$icon = Join-Path $src 'MdViewer.ico'
Compile @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    "/out:$exe", "/win32icon:$icon",
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    "/r:$(Join-Path $lib 'Microsoft.Web.WebView2.Core.dll')",
    "/r:$(Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll')",
    "/resource:$(Join-Path $src 'viewer.html'),viewer.html",
    "/resource:$(Join-Path $src 'viewer.css'),viewer.css",
    "/resource:$(Join-Path $obj 'highlight.css'),highlight.css",
    "/resource:$(Join-Path $lib 'markdown-it.min.js'),markdown-it.min.js",
    "/resource:$(Join-Path $lib 'highlight.min.js'),highlight.min.js",
    (Join-Path $src 'MdViewer.cs')
)
Write-Host "완료: $exe"

# 설치 파일: 위 결과물을 리소스로 품은 exe 하나
$setup = Join-Path $dist 'MdViewer-Setup.exe'
$res = @("/resource:$exe,MdViewer.exe") + ($dlls | ForEach-Object { "/resource:$(Join-Path $lib $_),$_" })
Compile (@(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    "/out:$setup", "/win32icon:$icon",
    '/r:System.Windows.Forms.dll'
) + $res + @((Join-Path $src 'Setup.cs')))
Write-Host "완료: $setup"
