# Exercise the exact batch quoting/view fallback using a harmless fake reg.exe.
# No real registry keys are changed. The fake SystemRoot applies only to child cmd processes.
param([string]$Compiler)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $Compiler){$Compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';if(-not(Test-Path -LiteralPath $Compiler)){$Compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('runtime detection '+[guid]::NewGuid())
[void][IO.Directory]::CreateDirectory((Join-Path $root 'System32'))
try {
 $source=@'
using System;
class FakeReg {
 static int Main(string[] args){string view="native";foreach(string a in args)if(a.StartsWith("/reg:"))view=a.Substring(5);
 string value=Environment.GetEnvironmentVariable("IT_RUNTIME_FIXTURE_"+view);if(String.IsNullOrEmpty(value)||value=="missing")return 1;
 if(value=="wrongtype"){Console.WriteLine("    Release    REG_SZ    999999");return 0;}
 Console.WriteLine("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full");Console.WriteLine("    Release    REG_DWORD    "+value);return 0;
 }
}
'@
 $src=Join-Path $root 'fake.cs';[IO.File]::WriteAllText($src,$source)
 & $Compiler /nologo /target:exe "/out:$root\System32\reg.exe" $src
 if($LASTEXITCODE -ne 0){throw 'Fake registry reader compilation failed'}
 foreach($case in @(
  @('hex45','0x5c615','missing','missing',0,'4.5'),
  @('view32','missing','0x80ea8','missing',0,'4.8'),
  @('highest','0x5c615','0x80ea8','missing',0,'4.8'),
  @('native','missing','missing','528040',0,'4.8'),
  @('old','378388','missing','missing',5,'below'),
  @('unread','missing','missing','missing',5,'Could not read'),
  @('type','wrongtype','wrongtype','wrongtype',5,'Could not read')
 )) {
  $info=New-Object Diagnostics.ProcessStartInfo
  $info.FileName=$env:ComSpec;$info.Arguments='/d /c call "'+(Join-Path $repo 'Check_Runtime.bat')+'"';$info.UseShellExecute=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.CreateNoWindow=$true
  $info.EnvironmentVariables['SystemRoot']=$root
  $info.EnvironmentVariables['IT_RUNTIME_FIXTURE_64']=$case[1];$info.EnvironmentVariables['IT_RUNTIME_FIXTURE_32']=$case[2];$info.EnvironmentVariables['IT_RUNTIME_FIXTURE_native']=$case[3]
  $process=New-Object Diagnostics.Process;$process.StartInfo=$info
  try{[void]$process.Start();$output=$process.StandardOutput.ReadToEnd();$errors=$process.StandardError.ReadToEnd();$process.WaitForExit();if($process.ExitCode -ne $case[4] -or -not $output.Contains($case[5])){throw ($case[0]+': exit='+$process.ExitCode+' '+$output+' '+$errors)}}finally{$process.Dispose()}
  Write-Output ('PASS exact batch runtime detection: '+$case[0])
 }
}finally{[IO.Directory]::Delete($root,$true)}
