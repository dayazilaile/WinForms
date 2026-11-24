param([string]$ScratchDirectory=[IO.Path]::GetTempPath())
& (Join-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) 'EnvironmentInlineTests.ps1') -ScratchDirectory $ScratchDirectory
