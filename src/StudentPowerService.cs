using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinForms
{
    public enum StudentPowerAction { Shutdown, Restart, Abort }
    public sealed class StudentPowerPlan
    {
        public readonly StudentPowerAction Action;public readonly int Seconds;public readonly IList<StudentMachine> Targets;
        public StudentPowerPlan(StudentPowerAction action,int seconds,List<StudentMachine> targets){Action=action;Seconds=seconds;Targets=targets.AsReadOnly();}
        public string Title {get{return Action==StudentPowerAction.Shutdown?"关机":Action==StudentPowerAction.Restart?"重启":"取消待执行关机/重启";}}
    }
    public sealed class StudentPowerResult
    {
        public StudentMachine Machine;public StudentPowerAction Action;public bool Accepted;public string Message;public int Error;
        public string Summary {get{return Message??"未完成";}}
    }
    public sealed class StudentPowerBatch
    {
        public int Total,Completed,Accepted;public bool Stopped;
    }
    public static class StudentPowerService
    {
        private static readonly SemaphoreSlim queue=new SemaphoreSlim(1,1);
        public static IEnumerable<string> LocalAddresses()
        {
            var addresses=new List<string>();foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())foreach(var address in nic.GetIPProperties().UnicastAddresses)if(address.Address.AddressFamily==AddressFamily.InterNetwork)addresses.Add(address.Address.ToString());return addresses;
        }
        public static StudentPowerPlan Prepare(IEnumerable<StudentMachine> machines,WakeNetwork network,StudentPowerAction action,int seconds,IEnumerable<string> localAddresses)
        {
            if(network==null||machines==null||localAddresses==null)throw new ArgumentException("请先选择本机网卡和学生机。");
            if(!Enum.IsDefined(typeof(StudentPowerAction),action)||(action==StudentPowerAction.Abort?seconds!=0:seconds<30||seconds>600))throw new ArgumentException("远程电源动作无效，倒计时需为 30–600 秒。");
            var local=new HashSet<string>(localAddresses,StringComparer.Ordinal);local.Add(network.Source.ToString());
            var macs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var ips=new HashSet<string>(StringComparer.Ordinal);var targets=new List<StudentMachine>();
            byte[] source=network.Source.GetAddressBytes(),mask=network.Mask.GetAddressBytes(),net=new byte[4];for(int i=0;i<4;i++)net[i]=(byte)(source[i]&mask[i]);string subnet=new IPAddress(net).ToString();
            foreach(var raw in machines){var machine=StudentRoster.Validate(raw);
                if(machine.IPv4.Length==0)throw new ArgumentException(machine.Name+" 没有 IP，请先扫描并保存当前地址。");
                var address=StudentWakeService.HostAddress(machine.IPv4);byte first=address.GetAddressBytes()[0];
                if(first==0||first==127||first>=224||local.Contains(machine.IPv4))throw new ArgumentException("不能对本机、回环或特殊地址执行学生机电源操作："+machine.IPv4);
                if(!network.Contains(machine.IPv4)||machine.IPv4==network.Broadcast.ToString()||machine.IPv4==subnet)throw new ArgumentException(machine.Name+" 不在所选网卡的有效主机范围内。");
                if(!macs.Add(machine.Mac)||!ips.Add(machine.IPv4))throw new ArgumentException("目标有重复 MAC/IP，请核对清单后再执行。");
                targets.Add(machine);if(targets.Count>StudentRoster.Maximum)throw new ArgumentException("一次最多操作 "+StudentRoster.Maximum+" 台。");
            }
            if(targets.Count==0)throw new ArgumentException("请先选中学生机，或选择具体机房。");return new StudentPowerPlan(action,seconds,targets);
        }
        public static Task<StudentPowerBatch> Run(IEnumerable<StudentMachine> machines,WakeNetwork network,StudentPowerAction action,int seconds,CancellationToken token,Action<StudentPowerResult> receive)
        {return RunCore(Prepare(machines,network,action,seconds,LocalAddresses()),network,token,ResolveMac,NativeRequest,receive);}
        internal static async Task<StudentPowerBatch> RunCore(StudentPowerPlan plan,WakeNetwork network,CancellationToken token,Func<string,WakeNetwork,CancellationToken,Task<string>> resolve,Func<StudentMachine,StudentPowerAction,int,int> execute,Action<StudentPowerResult> receive)
        {
            await queue.WaitAsync(token);try{return await RunSerialized(plan,network,token,resolve,execute,receive);}finally{queue.Release();}
        }
        private static async Task<StudentPowerBatch> RunSerialized(StudentPowerPlan plan,WakeNetwork network,CancellationToken token,Func<string,WakeNetwork,CancellationToken,Task<string>> resolve,Func<StudentMachine,StudentPowerAction,int,int> execute,Action<StudentPowerResult> receive)
        {
            token.ThrowIfCancellationRequested();var batch=new StudentPowerBatch{Total=plan.Targets.Count};
            foreach(var machine in plan.Targets){if(token.IsCancellationRequested){batch.Stopped=true;break;}
                var result=new StudentPowerResult{Machine=machine,Action=plan.Action};
                using(var audit=OperationAudit.Begin("学生机远程"+plan.Title,"目标机房="+machine.Room+"；名称="+machine.Name+"；IP="+machine.IPv4+"；MAC="+machine.Mac)){
                    try{if(!audit.StartWritten&&plan.Action!=StudentPowerAction.Abort)throw new IOException("软件目录操作日志不可写，未发送远程电源请求。");string actual=await resolve(machine.IPv4,network,token);token.ThrowIfCancellationRequested();
                        if(StudentWakeService.NormalizeMac(actual)!=machine.Mac)throw new InvalidOperationException("IP 对应的 MAC 与记录不符，未发送。请重新扫描核对。");
                        // Do not abandon an in-flight native RPC: a late accepted request must be reported/logged.
                        int code=await Task.Run(delegate{return execute(machine,plan.Action,plan.Seconds);});result.Error=code;result.Accepted=code==0;
                        result.Message=code==0?(plan.Action==StudentPowerAction.Abort?"目标已接受取消请求；未核实最终状态":"目标已接受请求，倒计时 "+plan.Seconds+" 秒；未确认实际完成"):ErrorText(code);
                        audit.End(result.Accepted?"目标已接受请求":"失败",result.Message);
                        if(!audit.EndWritten)result.Message+="；结果日志未写入，请保留此条界面结果。";
                    }catch(OperationCanceledException){audit.End("已停止","后续不再发送；已接受请求需另行取消。");batch.Stopped=true;break;}
                    catch(Exception ex){result.Message="未发送/失败："+ex.Message;audit.End("未发送/失败",ex.Message);}
                }
                batch.Completed++;if(result.Accepted)batch.Accepted++;receive(result);
            }
            if(token.IsCancellationRequested)batch.Stopped=true;return batch;
        }
        public static string ErrorText(int code)
        {
            string hint=code==5||code==1314?"权限不足：当前 Windows 账户需具备目标机远程关机权限":code==53?"找不到网络路径：请检查目标 SMB/远程管理通道；能扫描出 MAC 不代表通道已就绪":code==1722?"RPC 服务不可用：检查目标服务、网络隔离与相应防火墙规则":code==1190||code==1115?"目标已有待执行或正在进行的关机":code==1116?"目标没有可取消的关机请求":"远程请求失败";
            return "错误 "+code+"："+hint+"（"+new Win32Exception(code).Message+"）";
        }
        [DllImport("iphlpapi.dll",ExactSpelling=true)]private static extern uint SendARP(uint destination,uint source,[Out]byte[] mac,ref uint length);
        internal static Task<string> ResolveMac(string ip,WakeNetwork network,CancellationToken token)
        {return Task.Run(delegate{token.ThrowIfCancellationRequested();byte[] mac=new byte[6];uint length=6;uint code=SendARP(BitConverter.ToUInt32(IPAddress.Parse(ip).GetAddressBytes(),0),BitConverter.ToUInt32(network.Source.GetAddressBytes(),0),mac,ref length);token.ThrowIfCancellationRequested();if(code!=0||length!=6)throw new InvalidOperationException("无法核对目标 MAC，未发送（ARP 错误 "+code+"）。");return StudentWakeService.NormalizeMac(BitConverter.ToString(mac));});}
        [DllImport("advapi32.dll",EntryPoint="InitiateSystemShutdownExW",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
        [return:MarshalAs(UnmanagedType.Bool)]private static extern bool InitiateSystemShutdownEx(string machine,string message,uint seconds,[MarshalAs(UnmanagedType.Bool)]bool force,[MarshalAs(UnmanagedType.Bool)]bool reboot,uint reason);
        [DllImport("advapi32.dll",EntryPoint="AbortSystemShutdownW",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
        [return:MarshalAs(UnmanagedType.Bool)]private static extern bool AbortSystemShutdown(string machine);
        private static int NativeRequest(StudentMachine machine,StudentPowerAction action,int seconds)
        {
            foreach(string local in LocalAddresses())if(local==machine.IPv4)throw new InvalidOperationException("目标 IP 现已属于本机，未发送远程电源请求。");
            using(var privilege=new RemotePrivilege()){
                string target=@"\\"+machine.IPv4;bool ok=action==StudentPowerAction.Abort?AbortSystemShutdown(target):InitiateSystemShutdownEx(target,"WinForms 管理员计划"+(action==StudentPowerAction.Restart?"重启":"关机")+"，请保存工作。",(uint)seconds,false,action==StudentPowerAction.Restart,0x80040001);
                int code=Marshal.GetLastWin32Error();return ok?0:(code==0?1:code);
            }
        }
        [StructLayout(LayoutKind.Sequential)]private struct Luid{public uint Low;public int High;}
        [StructLayout(LayoutKind.Sequential)]private struct TokenPrivileges{public uint Count;public Luid Id;public uint Attributes;}
        [DllImport("kernel32.dll")]private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
        [DllImport("advapi32.dll",EntryPoint="LookupPrivilegeValueW",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool LookupPrivilegeValue(string system,string name,out Luid id);
        [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AdjustTokenPrivileges(IntPtr token,[MarshalAs(UnmanagedType.Bool)]bool disableAll,ref TokenPrivileges state,uint size,out TokenPrivileges previous,out uint returned);
        private sealed class RemotePrivilege:IDisposable
        {
            private IntPtr token;private TokenPrivileges previous;private bool restore;
            public RemotePrivilege(){Luid id;if(!OpenProcessToken(GetCurrentProcess(),0x28,out token)||!LookupPrivilegeValue(null,"SeRemoteShutdownPrivilege",out id))return;var desired=new TokenPrivileges{Count=1,Id=id,Attributes=2};uint returned;restore=AdjustTokenPrivileges(token,false,ref desired,(uint)Marshal.SizeOf(typeof(TokenPrivileges)),out previous,out returned)&&Marshal.GetLastWin32Error()==0;}
            public void Dispose(){if(token==IntPtr.Zero)return;try{if(restore){TokenPrivileges unused;uint returned;AdjustTokenPrivileges(token,false,ref previous,(uint)Marshal.SizeOf(typeof(TokenPrivileges)),out unused,out returned);}}finally{CloseHandle(token);token=IntPtr.Zero;}}
        }
    }
}
