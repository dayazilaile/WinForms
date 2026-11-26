using System;
using System.Text.RegularExpressions;

namespace WinForms
{
    public static partial class AutomationService
    {
        public static string CheckRenameName(string value)
        {
            string name=value??"";if(name.Length==0||name.Length>255||name!=name.Trim()||name=="."||name==".."||name.EndsWith(".",StringComparison.Ordinal)||name.IndexOfAny(new[]{'\\','/',':','*','?','"','<','>','|','\0','\r','\n'})>=0)throw new ArgumentException("新名称只填写名字，不包含路径、通配符、首尾空格或末尾点。");
            foreach(char c in name)if(Char.IsControl(c))throw new ArgumentException("新名称不能含控制字符。");
            if(Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\..*)?$",RegexOptions.IgnoreCase))throw new ArgumentException("新名称不能使用Windows保留设备名称。");return name;
        }
        public static string CheckRenamePath(string value)
        {
            string path=(value??"").Trim().Replace('/','\\');if(!Regex.IsMatch(path,@"^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\).+")||path.IndexOfAny(new[]{'*','?','"','<','>','|','\0','\r','\n'})>=0||path.EndsWith(":",StringComparison.Ordinal))throw new ArgumentException("请选择完整文件/文件夹路径，不允许盘符根目录、通配符或设备路径。");
            if(path.StartsWith(@"\\.\",StringComparison.Ordinal)||path.StartsWith(@"\\?\",StringComparison.Ordinal))throw new ArgumentException("不支持设备路径。");path=path.TrimEnd('\\');if(Regex.IsMatch(path,@"^[A-Za-z]:$")||Regex.IsMatch(path,@"^\\\\[^\\]+\\[^\\]+$"))throw new ArgumentException("禁止重命名盘符或共享根目录。");string rest=Regex.IsMatch(path,@"^[A-Za-z]:")?path.Substring(2):path;if(rest.IndexOf(':')>=0)throw new ArgumentException("不支持备用数据流路径。");return path;
        }
        public static void CheckRenameAction(AutoStep step)
        {if(step.Type=="重命名文件"||step.Type=="重命名文件夹"){CheckRenamePath(step.A);CheckRenameName(step.B);if(!String.IsNullOrWhiteSpace(step.C))throw new ArgumentException("重命名动作的参数3应留空。");}}
        private const string RenameActionHelper=@"
function Assert-ITRenameLocation([string]$path) {
 $full=[IO.Path]::GetFullPath($path).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
 $root=[IO.Path]::GetPathRoot([IO.Path]::GetFullPath($path)).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
 if($full -ieq $root){throw '禁止重命名盘符/共享根目录'}
 $folder=Get-Item -LiteralPath ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path))) -Force -ErrorAction Stop
 while($null -ne $folder){if(($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw '路径经过链接/重解析点，拒绝重命名'};$folder=$folder.Parent}
 $protected=@()
 foreach($kind in @('Windows','System','ProgramFiles','ProgramFilesX86','CommonApplicationData','UserProfile','DesktopDirectory','CommonDesktopDirectory','MyDocuments','MyMusic','MyPictures','MyVideos','ApplicationData','LocalApplicationData','StartMenu','Programs','Startup')) {
  $special=[Environment]::GetFolderPath([Environment+SpecialFolder]::$kind)
  if(-not [string]::IsNullOrWhiteSpace($special)){$protected+=@([IO.Path]::GetFullPath($special).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar))}
 }
 foreach($item in $protected){if($full -ieq $item -or $item.StartsWith($full+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw ('禁止重命名系统/用户标准目录或其父目录：'+$full)}}
 foreach($kind in @('Windows','System','ProgramFiles','ProgramFilesX86','CommonApplicationData')) {
  $special=[Environment]::GetFolderPath([Environment+SpecialFolder]::$kind)
  if(-not [string]::IsNullOrWhiteSpace($special)){$special=[IO.Path]::GetFullPath($special).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar);if($full.StartsWith($special+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '系统/程序安装目录内不执行自动重命名'}}
 }
 if(-not [string]::IsNullOrWhiteSpace($env:IT_TOOLKIT_BAT)){$scriptPath=[IO.Path]::GetFullPath($env:IT_TOOLKIT_BAT);if($scriptPath -ieq $full -or $scriptPath.StartsWith($full+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '不能重命名当前BAT或它所在的父目录'}}
}
function Invoke-ITRenameItem([string]$path,[string]$name,[bool]$directory) {
 if([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::GetFileName($name) -cne $name -or $name -match '[\x00-\x1f<>:""/\\|?*]' -or $name -match '[. ]$' -or $name -eq '.' -or $name -eq '..' -or $name -match '^(?i:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\..*)?$'){throw '新名称无效'}
 $source=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 if($source.PSProvider.Name -ne 'FileSystem' -or [bool]$source.PSIsContainer -ne $directory){throw '目标类型不符（文件/文件夹），未修改'}
 if(($source.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw '不重命名链接/重解析点'}
 Assert-ITRenameLocation $source.FullName
 if($source.Name -ceq $name){Write-Host ('[OK] 已经是该名称：'+$source.FullName);return}
 if($directory){$parent=$source.Parent.FullName}else{$parent=$source.DirectoryName}
 $old=$source.FullName;$target=[IO.Path]::Combine($parent,$name)
 $caseOnly=[string]::Equals($old,$target,[StringComparison]::OrdinalIgnoreCase)
 if(-not $caseOnly -and (Test-Path -LiteralPath $target)){throw ('目标名称已存在，未覆盖：'+$target)}
 Rename-Item -LiteralPath $old -NewName $name -ErrorAction Stop
 $after=Get-Item -LiteralPath $target -Force -ErrorAction Stop
 if($after.Name -cne $name -or (-not $caseOnly -and (Test-Path -LiteralPath $old))){throw '重命名后核验不符，停止后续动作'}
 Write-Host ('[OK] 已重命名（未移动/覆盖）：'+$old+' -> '+$after.FullName)
}
";
    }
}
