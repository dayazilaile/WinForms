using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace WinForms
{
    internal static class OptimizationPower
    {
        internal const string High="8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        internal sealed class Setting
        {
            internal string Key;internal Guid Group,Id;internal uint Desired;internal bool LaptopOnly;
            internal Setting(string key,string group,string id,uint desired,bool laptop=false){Key=key;Group=new Guid(group);Id=new Guid(id);Desired=desired;LaptopOnly=laptop;}
        }
        internal static readonly Setting[] Settings={
            new Setting("screen","7516b95f-f776-4464-8c53-06167f40cc99","3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e",0),
            new Setting("sleep","238c9fa8-0aad-41ed-83f4-97be242c8f20","29f6c1db-86da-48c5-9fdb-f2b67b1f44da",0),
            new Setting("hibernate","238c9fa8-0aad-41ed-83f4-97be242c8f20","9d7815a6-7ee4-497e-8888-515a05f02364",0),
            new Setting("unattended","238c9fa8-0aad-41ed-83f4-97be242c8f20","7bc4a2f9-d8fc-4469-b07b-33eb785aaca0",0),
            new Setting("cpuMin","54533251-82be-4824-96c1-47b60b740d00","893dee8e-2bef-41e0-89c6-b55d0929964c",100),
            new Setting("cpuMax","54533251-82be-4824-96c1-47b60b740d00","bc5038f7-23e0-4960-96da-33abaf5935ec",100),
            new Setting("lid","4f971e89-eebd-4455-a8de-9e59040e7347","5ca83367-6e45-459f-a27b-476b1d01c936",0,true)
        };
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerSetActiveScheme(IntPtr root,ref Guid scheme);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerEnumerate(IntPtr root,IntPtr scheme,IntPtr group,uint access,uint index,byte[] buffer,ref uint size);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerReadFriendlyName(IntPtr root,ref Guid scheme,IntPtr group,IntPtr setting,byte[] buffer,ref uint size);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerWriteFriendlyName(IntPtr root,ref Guid scheme,IntPtr group,IntPtr setting,byte[] buffer,uint size);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerReadACValueIndex(IntPtr root,ref Guid scheme,ref Guid group,ref Guid setting,out uint value);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerReadDCValueIndex(IntPtr root,ref Guid scheme,ref Guid group,ref Guid setting,out uint value);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerWriteACValueIndex(IntPtr root,ref Guid scheme,ref Guid group,ref Guid setting,uint value);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerWriteDCValueIndex(IntPtr root,ref Guid scheme,ref Guid group,ref Guid setting,uint value);
        [DllImport("powrprof.dll",ExactSpelling=true)]private static extern uint PowerDeleteScheme(IntPtr root,ref Guid scheme);
        [DllImport("kernel32.dll",ExactSpelling=true)]private static extern IntPtr LocalFree(IntPtr memory);
        private static void Check(uint code,string operation){if(code!=0)throw new InvalidOperationException(operation+"失败，Windows错误 "+code+"；可能受硬件或系统策略限制。");}
        internal static Guid Active(){IntPtr pointer=IntPtr.Zero;try{Check(PowerGetActiveScheme(IntPtr.Zero,out pointer),"读取活动电源计划");if(pointer==IntPtr.Zero)throw new InvalidOperationException("没有返回电源计划。");return (Guid)Marshal.PtrToStructure(pointer,typeof(Guid));}finally{if(pointer!=IntPtr.Zero)LocalFree(pointer);}}
        internal static List<Guid> Schemes(){var result=new List<Guid>();for(uint i=0;i<1024;i++){uint size=16;var data=new byte[16];uint code=PowerEnumerate(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,16,i,data,ref size);if(code==259)return result;Check(code,"枚举电源计划");if(size!=16)throw new InvalidOperationException("电源计划GUID长度无效。");result.Add(new Guid(data));}throw new InvalidOperationException("电源计划数量异常。");}
        internal static string Name(Guid scheme){uint size=0;uint code=PowerReadFriendlyName(IntPtr.Zero,ref scheme,IntPtr.Zero,IntPtr.Zero,null,ref size);if(code!=0&&code!=234)Check(code,"读取计划名称");if(size<2||size>4096)throw new InvalidOperationException("计划名称长度异常。");var buffer=new byte[size];Check(PowerReadFriendlyName(IntPtr.Zero,ref scheme,IntPtr.Zero,IntPtr.Zero,buffer,ref size),"读取计划名称");return Encoding.Unicode.GetString(buffer,0,(int)size).TrimEnd('\0');}
        private static void Name(Guid scheme,string value){byte[] buffer=Encoding.Unicode.GetBytes(value+"\0");Check(PowerWriteFriendlyName(IntPtr.Zero,ref scheme,IntPtr.Zero,IntPtr.Zero,buffer,(uint)buffer.Length),"写入计划名称");}
        private static bool Read(Guid scheme,Setting item,bool dc,out uint value){return (dc?PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref item.Group,ref item.Id,out value):PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref item.Group,ref item.Id,out value))==0;}
        private static void Write(Guid scheme,Setting item,bool dc,uint value){Check(dc?PowerWriteDCValueIndex(IntPtr.Zero,ref scheme,ref item.Group,ref item.Id,value):PowerWriteACValueIndex(IntPtr.Zero,ref scheme,ref item.Group,ref item.Id,value),item.Key+(dc?"电池":"插电"));}
        internal static Dictionary<string,string> Capture(bool? laptop)
        {
            var active=Active();var schemes=Schemes();Guid target=Guid.Empty;var high=new Guid(High);if(schemes.Contains(high))target=high;else foreach(var s in schemes)if(Name(s)=="高性能"){target=s;break;}
            bool created=target==Guid.Empty;if(created)target=Guid.NewGuid();var result=new Dictionary<string,string>{{"active",active.ToString()},{"target",target.ToString()},{"created",created?"1":"0"},{"laptop",laptop.HasValue?(laptop.Value?"1":"0"):"unknown"}};
            if(!created){result["name"]=Name(target);foreach(var item in Settings)if(!item.LaptopOnly||laptop==true)foreach(bool dc in new[]{false,true}){uint value;string key=item.Key+(dc?".dc":".ac");result[key]=Read(target,item,dc,out value)?value.ToString(CultureInfo.InvariantCulture):"unsupported";}}
            return result;
        }
        internal static string Apply(Dictionary<string,string> before,Func<string,string,string> command)
        {
            var target=new Guid(before["target"]);bool created=before["created"]=="1";string note="";
            if(created){string output=command("powercfg.exe","/duplicatescheme "+High+" "+target);if(!Schemes().Contains(target)){output=command("powercfg.exe","/duplicatescheme "+new Guid(before["active"])+" "+target);note="系统高性能模板不可用，已尝试复制原计划并配置CPU上下限100%。\r\n";}if(!Schemes().Contains(target))throw new InvalidOperationException("无法创建高性能计划；未切换原计划。\r\n"+output);}
            Name(target,"高性能");var skipped=new List<string>();
            foreach(var item in Settings)if(item.Key=="screen"||item.Key=="sleep"||item.Key=="cpuMin"||item.Key=="cpuMax"){uint value;if(!Read(target,item,false,out value))throw new NotSupportedException("该设备无法读取高性能/屏幕/睡眠必要参数："+item.Key+"。已保存备份，可恢复检查。");}
            foreach(var item in Settings)if(!item.LaptopOnly||before["laptop"]=="1")foreach(bool dc in new[]{false,true}){uint old;string key=item.Key+(dc?".dc":".ac");if((!created&&before[key]=="unsupported")||(created&&!Read(target,item,dc,out old))){skipped.Add(key);continue;}Write(target,item,dc,item.Desired);}
            Check(PowerSetActiveScheme(IntPtr.Zero,ref target),"启用高性能计划");
            return note+"已尝试启用“高性能”，所有可用AC/DC屏幕、睡眠、自动休眠超时为0；可用CPU上下限100%。设备类型："+before["laptop"]+"。\r\n不支持的参数："+(skipped.Count==0?"无":String.Join("、",skipped))+"。低电量/过热保护未改。";
        }
        internal static void Restore(Dictionary<string,string> before)
        {
            var target=new Guid(before["target"]);var active=new Guid(before["active"]);bool created=before["created"]=="1";var schemes=Schemes();if(!schemes.Contains(active))throw new InvalidOperationException("原电源计划已不存在，未猜测替代计划。");
            if(!created){if(!schemes.Contains(target))throw new InvalidOperationException("待恢复的高性能计划已不存在。");foreach(var item in Settings)if(!item.LaptopOnly||before["laptop"]=="1")foreach(bool dc in new[]{false,true}){string value=before[item.Key+(dc?".dc":".ac")];if(value!="unsupported")Write(target,item,dc,UInt32.Parse(value,CultureInfo.InvariantCulture));}Name(target,before["name"]);}
            Check(PowerSetActiveScheme(IntPtr.Zero,ref active),"恢复原活动计划");if(created&&schemes.Contains(target))Check(PowerDeleteScheme(IntPtr.Zero,ref target),"删除本次创建的计划");
        }
        internal static string Verify(Dictionary<string,string> before,bool restored)
        {
            var target=new Guid(before["target"]);var active=new Guid(before["active"]);bool created=before["created"]=="1";
            if(Active()!=(restored?active:target))throw new InvalidOperationException("活动计划核验不符。");if(restored&&created){if(Schemes().Contains(target))throw new InvalidOperationException("本次新建计划尚未删除。");return "原活动电源计划已核验，新建计划已移除。";}
            if(Name(target)!=(restored?before["name"]:"高性能"))throw new InvalidOperationException("电源计划名称核验不符。");
            foreach(var item in Settings)if(!item.LaptopOnly||before["laptop"]=="1")foreach(bool dc in new[]{false,true}){string key=item.Key+(dc?".dc":".ac");if(!created&&before[key]=="unsupported")continue;uint value;if(!Read(target,item,dc,out value)){if(created)continue;throw new InvalidOperationException("无法核验参数："+key);}uint wanted=restored?UInt32.Parse(before[key],CultureInfo.InvariantCulture):item.Desired;if(value!=wanted)throw new InvalidOperationException("电源参数核验不符："+key);}
            return "已重新读取活动计划及可用AC/DC参数；实际睡眠仍可能受组织策略/厂商软件影响。";
        }
    }
}
