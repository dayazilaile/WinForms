# Windows PowerShell 5.1
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & cmd.exe /d /c 'Build_Only.bat --no-pause'
    if ($LASTEXITCODE -ne 0) { throw 'Standalone application compilation failed' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    foreach ($name in @('StandaloneTests', 'StandaloneUiTests')) {
        & $compiler /nologo /langversion:5 /codepage:65001 "/out:build\$name.exe" /reference:build\BatAutomation.exe /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "tests\$name.cs"
        if ($LASTEXITCODE -ne 0) { throw ($name + ' compilation failed') }
    }
    & .\build\StandaloneTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Standalone tests failed' }
    $clean = Join-Path ([IO.Path]::GetTempPath()) ('bat-standalone-ui-' + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $clean | Out-Null
    try {
        Copy-Item build\BatAutomation.exe, build\StandaloneUiTests.exe -Destination $clean
        Push-Location $clean
        try { & .\StandaloneUiTests.exe; if ($LASTEXITCODE -ne 0) { throw 'Standalone UI tests failed' } }
        finally { Pop-Location }
        Copy-Item (Join-Path $clean '*.png') -Destination build
    }
    finally { Remove-Item -LiteralPath $clean -Recurse -Force }
    foreach ($file in Get-ChildItem -LiteralPath build\generated-tests -Filter *.ps1) {
        $tokens = $null; $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw ($file.Name + ': ' + ($errors | Out-String)) }
        Write-Output ('PASS PowerShell parse: ' + $file.Name)
    }
    $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\success.ps1 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or -not $result.Contains('All steps complete.') -or $result.Contains('FAILED:')) { throw ('Conditional success scenario failed: ' + $result) }
    $ErrorActionPreference = 'Continue'
    try { $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\failure.ps1 2>&1 | Out-String) }
    finally { $ErrorActionPreference = 'Stop' }
    if ($result.Contains('All steps complete.') -or -not $result.Contains('FAILED:') -or [regex]::Matches($result, '步骤 2/3').Count -ne 2) { throw ('Retry fixture failed: ' + $result) }
    Write-Output 'PASS PowerShell conditional success and bounded retry failure fixtures'
    $stage = Join-Path $repo 'build\portable'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item build\BatAutomation.exe, README.md, START_HERE.txt, Build_Only.bat, Build_And_Run.bat, SOURCE_PROVENANCE.json -Destination $stage
    Copy-Item src, tests, docs, examples -Destination $stage -Recurse -Force
    $rows = Get-ChildItem $stage -File -Recurse | Where-Object { $_.FullName -ne (Join-Path $stage 'SHA256SUMS.txt') } | Sort-Object FullName | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.FullName.Substring($stage.Length + 1).Replace('\', '/') }
    [IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $rows, (New-Object Text.UTF8Encoding($false)))
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath build\BatAutomation-v0.5.10-BAT-Windows.zip -Force
    Write-Output 'Standalone Windows validation and package completed.'
}
finally { Pop-Location }
