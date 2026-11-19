# Harmless generated fixtures only; supports Windows PowerShell 5.1 and portable PowerShell.
param([string]$PowerShell = 'powershell.exe', [string]$GeneratedDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'build/generated-tests'))
$ErrorActionPreference = 'Stop'
foreach ($file in Get-ChildItem -LiteralPath $GeneratedDirectory -Filter *.ps1) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw ($file.Name + ': ' + ($errors | Out-String)) }
}
foreach ($case in @(@('bounded-checks-fixture.ps1', 0), @('false-condition-fixture.ps1', 0), @('wait-timeout-fixture.ps1', 1), @('condition-error-fixture.ps1', 1))) {
    $ErrorActionPreference = 'Continue'
    try { $result = (& $PowerShell -NoLogo -NoProfile -NonInteractive -File (Join-Path $GeneratedDirectory $case[0]) 2>&1 | Out-String); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = 'Stop' }
    if ($code -ne $case[1]) { throw ($case[0] + ': exit=' + $code + ': ' + $result) }
    if ($case[1] -eq 1 -and (-not $result.Contains('FAILED:') -or $result.Contains('All steps complete.') -or $result.Contains('重试块失败') -or ($case[0] -eq 'condition-error-fixture.ps1' -and $result.Contains('步骤 ')))) { throw ($case[0] + ': actions continued after terminal timeout/error: ' + $result) }
    if ($case[0] -eq 'bounded-checks-fixture.ps1' -and -not $result.Contains('PASS bounded checks fixture')) { throw $result }
    if ($case[0] -eq 'false-condition-fixture.ps1' -and -not $result.Contains('All steps complete.')) { throw $result }
    Write-Output ('PASS timeout runtime: ' + $case[0])
}
