# Windows PowerShell 5.1 test entry for an approved development/CI session.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & .\tests\Run-EnvironmentPreparation.ps1
    & .\tests\Run-EnvironmentBatch.ps1
    & .\tests\Run-RuntimeDetection.ps1
    & cmd.exe /d /c 'Build_Only.bat --no-pause'
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    & $compiler /nologo /codepage:65001 /out:build\FeatureTests.exe /reference:build\WinForms.exe /reference:System.Core.dll tests\FeatureTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & .\build\FeatureTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Feature tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\NetworkToolsTests.exe /reference:build\WinForms.exe /reference:System.Core.dll /reference:System.Xml.dll tests\NetworkToolsTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Network tools test compilation failed' }
    & .\build\NetworkToolsTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Network tools tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\WorkspaceTests.exe /reference:build\WinForms.exe /reference:System.Core.dll tests\WorkspaceTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Workspace test compilation failed' }
    & .\build\WorkspaceTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Workspace tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\TrialTests.exe /reference:build\WinForms.exe /reference:System.Core.dll tests\TrialTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Trial test compilation failed' }
    & .\build\TrialTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Trial tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\DesktopTests.exe /reference:build\WinForms.exe /reference:System.Core.dll tests\DesktopTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Desktop test compilation failed' }
    & .\build\DesktopTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\StudentWakeTests.exe /reference:build\WinForms.exe /reference:System.Core.dll tests\StudentWakeTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Student wake test compilation failed' }
    & .\build\StudentWakeTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Student wake tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\NavigationUiTests.exe /reference:build\WinForms.exe /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll tests\NavigationUiTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed' }
    & .\build\NavigationUiTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed' }
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
    foreach ($fixture in @('trial-dns-fixture.ps1', 'trial-connections-fixture.ps1', 'configuration-fixture.ps1', 'random-wait-fixture.ps1', 'software-cleanup-fixture.ps1', 'random-ip-fixture.ps1', 'legacy-configuration-fixture.ps1', 'portable-network-backup-fixture.ps1', 'network-audit-fixture.ps1')) {
        $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File (Join-Path 'build\generated-tests' $fixture) 2>&1 | Out-String)
        if ($LASTEXITCODE -ne 0 -or -not ($result.Contains('PASS trial fixture') -or $result.Contains('PASS random wait fixture') -or $result.Contains('PASS software cleanup fixture') -or $result.Contains('PASS random IPv4 fixture') -or $result.Contains('PASS legacy configuration fixture') -or $result.Contains('PASS portable network backup fixture') -or $result.Contains('PASS network audit fixture'))) { throw ($fixture + ': ' + $result) }
        Write-Output $result.Trim()
    }
    $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\success.ps1 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or -not $result.Contains('All steps complete.') -or $result.Contains('FAILED:')) { throw ('Conditional success scenario failed: '+$result) }
    Write-Output 'PASS conditional branch executes and retry block succeeds'
    $ErrorActionPreference = 'Continue'
    try { $result = (& powershell.exe -NoLogo -NoProfile -NonInteractive -File build\generated-tests\failure.ps1 2>&1 | Out-String) }
    finally { $ErrorActionPreference = 'Stop' }
    if ($result.Contains('All steps complete.') -or -not $result.Contains('FAILED:')) { throw ('Failed retry incorrectly reported success: '+$result) }
    if ([regex]::Matches($result, '步骤 2/3').Count -ne 2) { throw ('Retry did not execute exactly twice: '+$result) }
    Write-Output 'PASS failed retry executes twice and does not report completion'
    Write-Output 'All automated checks completed. USB registry policies and printer queues were not modified.'
}
finally { Pop-Location }
