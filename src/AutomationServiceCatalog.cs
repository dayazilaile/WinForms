using System;
using System.Collections.Generic;
using System.ServiceProcess;
using System.Threading;

namespace WinForms
{
    public sealed class AutomationServiceRow
    {public string Name="",Display="",State="",Note="";public bool Selectable=true;}
    public sealed class AutomationServiceSnapshot
    {public readonly List<AutomationServiceRow> Rows=new List<AutomationServiceRow>();public int Unreadable;}
    public static class AutomationServiceCatalog
    {
        public static AutomationServiceSnapshot Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();var result=new AutomationServiceSnapshot();var services=ServiceController.GetServices();
            try{foreach(var service in services){token.ThrowIfCancellationRequested();try{var row=new AutomationServiceRow{Name=service.ServiceName,Display=service.DisplayName,State=service.Status.ToString()};
                try{AutomationService.CheckStopServiceName(row.Name);}catch(ArgumentException ex){row.Selectable=false;row.Note=ex.Message;}
                if(row.Selectable)row.Note=service.CanStop?"当前接受停止控制":"当前不接受停止/已停止，实际运行时重新核对";result.Rows.Add(row);
            }catch{result.Unreadable++;}}}finally{foreach(var service in services)service.Dispose();}
            result.Rows.Sort(delegate(AutomationServiceRow a,AutomationServiceRow b){return StringComparer.OrdinalIgnoreCase.Compare(a.Name,b.Name);});return result;
        }
    }
}
