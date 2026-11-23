# Windows PowerShell 5.1
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & .\tests\Run-RuntimeDetection.ps1
    & cmd.exe /d /c 'Build_Only.bat --no-pause'
    if ($LASTEXITCODE -ne 0) { throw 'Standalone application compilation failed' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    foreach ($name in @('StandaloneTests', 'StandaloneUiTests')) {
        & $compiler /nologo /langversion:5 /codepage:65001 "/out:build\$name.exe" /reference:build\WinForms-BAT.exe /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "tests\$name.cs"
        if ($LASTEXITCODE -ne 0) { throw ($name + ' compilation failed') }
    }
    $published = Join-Path ([IO.Path]::GetTempPath()) ('bat-published-' + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $published | Out-Null
    try {
        Copy-Item WinForms-BAT.exe, build\StandaloneTests.exe, build\StandaloneUiTests.exe -Destination $published
        Push-Location $published
        try {
            & .\StandaloneTests.exe
            if ($LASTEXITCODE -ne 0) { throw 'Published EXE fails Windows automation tests' }
            & .\StandaloneUiTests.exe
            if ($LASTEXITCODE -ne 0) { throw 'Published EXE fails Windows UI tests' }
        }
        finally { Pop-Location }
    }
    finally { Remove-Item -LiteralPath $published -Recurse -Force }
    Write-Output 'PASS exact published EXE on Windows before testing locally rebuilt EXE'
    & .\build\StandaloneTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Standalone tests failed' }
    $clean = Join-Path ([IO.Path]::GetTempPath()) ('bat-standalone-ui-' + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $clean | Out-Null
    try {
        Copy-Item build\WinForms-BAT.exe, build\StandaloneUiTests.exe -Destination $clean
        Push-Location $clean
        try { & .\StandaloneUiTests.exe; if ($LASTEXITCODE -ne 0) { throw 'Standalone UI tests failed' } }
        finally { Pop-Location }
        Copy-Item (Join-Path $clean '*.png') -Destination build
    }
    finally { Remove-Item -LiteralPath $clean -Recurse -Force }
    & .\tests\Run-TimeoutFixtures.ps1
    # Exercise the exact visible cleanup block with harmless files and no automation payload.
    $cleanupFolder = Join-Path ([IO.Path]::GetTempPath()) ('visible-cleanup-' + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $cleanupFolder | Out-Null
    try {
        foreach ($expected in @(0, 7)) {
            foreach ($locked in @($false, $true)) {
                $fixture = Join-Path $cleanupFolder ("清理 O'Reilly &100%! (" + $expected + '-' + $locked + ').bat')
                Copy-Item -LiteralPath (Join-Path 'build\generated-tests' ('visible-cleanup-' + $expected + '.bat')) -Destination $fixture
                $env:IT_TOOLKIT_CLEANUP_FIXTURE = $fixture
                $held = $null
                try {
                    # Allow cmd to read the script but deny deletion for the failure case.
                    if ($locked) { $held = [IO.File]::Open($fixture, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read) }
                    $result = (& cmd.exe /d /s /c '""%IT_TOOLKIT_CLEANUP_FIXTURE%""' 2>&1 | Out-String)
                    $code = $LASTEXITCODE
                    if ($code -ne $expected -or (Test-Path -LiteralPath $fixture) -ne $locked -or $result.Contains('[WARNING]') -ne $locked) { throw ('Visible cleanup failed, exit=' + $code + ', locked=' + $locked + ': ' + $result) }
                    Write-Output ('PASS visible cleanup with original exit code ' + $expected + ', locked=' + $locked)
                }
                finally { if ($null -ne $held) { $held.Dispose() } }
            }
        }
    }
    finally { Remove-Item Env:IT_TOOLKIT_CLEANUP_FIXTURE -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $cleanupFolder -Recurse -Force }
    # Mock the shell call and token result: no UAC consent, real elevation or input actions.
    foreach ($case in @(@('request', 42), @('cancel', 1), @('already', 0))) {
        $ErrorActionPreference = 'Continue'
        try { $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File (Join-Path 'build\generated-tests' ('uac-' + $case[0] + '-fixture.ps1')) 2>&1 | Out-String); $code = $LASTEXITCODE }
        finally { $ErrorActionPreference = 'Stop' }
        if ($code -ne $case[1]) { throw ('Mock UAC ' + $case[0] + ' returned ' + $code + ': ' + $result) }
        if ($case[0] -eq 'request' -and -not $result.Contains('PASS mock standard UAC request')) { throw ('Mock UAC request did not validate its path: ' + $result) }
        if ($case[0] -eq 'cancel' -and -not $result.Contains('UAC elevation cancelled or failed: mock cancellation')) { throw ('Mock UAC cancellation was not reported: ' + $result) }
        Write-Output ('PASS mock UAC branch: ' + $case[0])
    }
    foreach ($file in Get-ChildItem -LiteralPath build\generated-tests -Filter *.ps1) {
        $tokens = $null; $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw ($file.Name + ': ' + ($errors | Out-String)) }
        Write-Output ('PASS PowerShell parse: ' + $file.Name)
    }
    $auditResult = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\network-audit-fixture.ps1 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or -not $auditResult.Contains('PASS network audit fixture')) { throw ('Network audit fixture failed: '+$auditResult) }
    $ipResult = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\random-ip-fixture.ps1 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or -not $ipResult.Contains('PASS random IPv4 fixture')) { throw ('Random IPv4 runtime fixture failed: ' + $ipResult) }
    Write-Output 'PASS mocked random IPv4 conflicts, rollback and both success modes'
    $randomResult = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\random-wait-fixture.ps1 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or -not $randomResult.Contains('PASS random wait fixture')) { throw ('Random-wait runtime fixture failed: ' + $randomResult) }
    Write-Output 'PASS runtime random-wait resampling and inclusive bounds without sleeping'
    $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\success.ps1 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or -not $result.Contains('All steps complete.') -or $result.Contains('FAILED:')) { throw ('Conditional success scenario failed: ' + $result) }
    $ErrorActionPreference = 'Continue'
    try { $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\failure.ps1 2>&1 | Out-String) }
    finally { $ErrorActionPreference = 'Stop' }
    if ($result.Contains('All steps complete.') -or -not $result.Contains('FAILED:') -or [regex]::Matches($result, '步骤 2/3').Count -ne 2) { throw ('Retry fixture failed: ' + $result) }
    Write-Output 'PASS PowerShell conditional success and bounded retry failure fixtures'
    $stage = Join-Path $repo 'build\portable'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item build\WinForms-BAT.exe, README.md, START_HERE.txt, Build_Only.bat, Build_And_Run.bat, SOURCE_PROVENANCE.json, BUILD_INFO.json, app.manifest, Check_Runtime.bat, Diagnose_Runtime.bat, release-policy.json -Destination $stage
    Copy-Item src, tests, docs, examples, tools -Destination $stage -Recurse -Force
    Move-Item (Join-Path $stage 'BUILD_INFO.json') (Join-Path $stage 'PUBLISHED_BUILD_INFO.json') -Force
    $windowsBuild = @{ assembly='WinForms-BAT.exe'; sha256=(Get-FileHash build\WinForms-BAT.exe -Algorithm SHA256).Hash.ToLowerInvariant(); compiler='Windows .NET Framework csc'; type_reference_check='Windows runtime tests passed'; source_package_build='PUBLISHED_BUILD_INFO.json' }
    [IO.File]::WriteAllText((Join-Path $stage 'BUILD_INFO.json'), ($windowsBuild | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    $rows = Get-ChildItem $stage -File -Recurse | Where-Object { $_.FullName -ne (Join-Path $stage 'SHA256SUMS.txt') } | Sort-Object FullName | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.FullName.Substring($stage.Length + 1).Replace('\', '/') }
    [IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $rows, (New-Object Text.UTF8Encoding($false)))
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath build\WinForms-BAT-v0.6.1-Windows.zip -Force
    Write-Output 'Standalone Windows validation and package completed.'
}
finally { Pop-Location }
