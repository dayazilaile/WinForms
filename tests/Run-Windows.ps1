# Windows PowerShell 5.1: powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-Windows.ps1
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & cmd.exe /d /c 'Build_Only.bat --no-pause'
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    & $compiler /nologo /codepage:65001 /out:build\FeatureTests.exe /reference:build\ITDesktopToolkit.exe /reference:System.Core.dll tests\FeatureTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & .\build\FeatureTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Feature tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\NetworkToolsTests.exe /reference:build\ITDesktopToolkit.exe /reference:System.Core.dll /reference:System.Xml.dll tests\NetworkToolsTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Network tools test compilation failed' }
    & .\build\NetworkToolsTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Network tools tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\WorkspaceTests.exe /reference:build\ITDesktopToolkit.exe /reference:System.Core.dll tests\WorkspaceTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Workspace test compilation failed' }
    & .\build\WorkspaceTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Workspace tests failed' }
    & $compiler /nologo /codepage:65001 /out:build\NavigationUiTests.exe /reference:build\ITDesktopToolkit.exe /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll tests\NavigationUiTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed' }
    & .\build\NavigationUiTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed' }
    foreach ($file in Get-ChildItem -LiteralPath build\generated-tests -Filter *.ps1) {
        $tokens = $null; $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
        if ($errors.Count -gt 0) { throw ($file.Name + ': ' + ($errors | Out-String)) }
        Write-Output ('PASS PowerShell parse: ' + $file.Name)
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
