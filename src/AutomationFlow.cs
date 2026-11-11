using System;
using System.Collections.Generic;
using System.Text;

namespace ITDesktopToolkit
{
    public static partial class AutomationService
    {
        private sealed class FlowBlock { public string Type; public int Id, Attempts, Delay; public bool HasElse; }
        public static void ValidateFlow(List<AutoStep> steps)
        {
            if(steps==null)throw new ArgumentNullException("steps");
            var stack=new Stack<FlowBlock>();var ignored=new StringBuilder();
            for(int i=0;i<steps.Count;i++)
            {
                AutoStep s=steps[i];if(s==null)throw new ArgumentException("步骤不能为空。");
                if(!EmitFlow(s,ignored,stack,i))
                    foreach(var f in stack)if(f.Type=="retry"&&(s.Type=="格式化指定卷"||s.Type=="清空当前用户回收站"||s.Type.StartsWith("删除",StringComparison.Ordinal)))
                        throw new ArgumentException("不允许在自动重试块中格式化或清理文件。请移至重试块之外。");
            }
            if(stack.Count>0)throw new ArgumentException("条件或重试块未结束，请添加对应的结束动作。");
        }
        private static int FlowInt(string s,int fallback,int max)
        { int n=PositiveInt(s,fallback,max);if(n<1)throw new ArgumentException("此参数必须大于零。");return n; }
        private static string FlowValue(string s)
        {if(String.IsNullOrWhiteSpace(s))throw new ArgumentException("条件参数不能为空。");return s;}
        private static bool EmitFlow(AutoStep s,StringBuilder b,Stack<FlowBlock> stack,int index)
        {
            string condition=null;
            switch(s.Type)
            {
                case "如果文件存在": condition="(Test-Path -LiteralPath "+P(FlowValue(s.A))+" -PathType Leaf)";break;
                case "如果网络可达":
                    NetworkMonitorService.Targets(FlowValue(s.A));
                    if(s.A.IndexOfAny(new char[]{',',';',' ','\r','\n'})>=0)throw new ArgumentException("网络条件只支持单个主机名/IP。");
                    condition="(Test-ITReachable "+P(s.A)+")";break;
                case "如果系统版本":
                    int minimum=FlowInt(s.A,10240,999999);int maximum=FlowInt(s.B,999999,999999);
                    if(minimum>maximum)throw new ArgumentException("系统版本下限不能大于上限。");
                    condition="($itWindowsBuild -ge "+minimum+" -and $itWindowsBuild -le "+maximum+")";break;
                case "否则":
                    if(stack.Count==0||stack.Peek().Type!="if"||stack.Peek().HasElse)throw new ArgumentException("『否则』必须位于尚未使用否则的条件块中。");
                    stack.Peek().HasElse=true;b.AppendLine("} else {");return true;
                case "结束条件":
                    if(stack.Count==0||stack.Peek().Type!="if")throw new ArgumentException("结束条件与开始不匹配。");
                    stack.Pop();b.AppendLine("}");return true;
                case "重试开始":
                    if(stack.Count>=8)throw new ArgumentException("条件/重试最多嵌套 8 层。");
                    var retry=new FlowBlock{Type="retry",Id=index,Attempts=FlowInt(s.A,3,10),Delay=FlowInt(s.B,2,300)};stack.Push(retry);
                    b.AppendLine("for($itRetry"+index+"=1;$itRetry"+index+" -le "+retry.Attempts+";$itRetry"+index+"++){ try {");return true;
                case "重试结束":
                    if(stack.Count==0||stack.Peek().Type!="retry")throw new ArgumentException("重试结束与开始不匹配。");
                    var r=stack.Pop();b.AppendLine("break\r\n} catch { if($itRetry"+r.Id+" -ge "+r.Attempts+"){throw}; Write-Host ('重试块失败，将重新执行：'+$_.Exception.Message); Start-Sleep -Seconds "+r.Delay+" } }");return true;
                default:return false;
            }
            if(stack.Count>=8)throw new ArgumentException("条件/重试最多嵌套 8 层。");
            stack.Push(new FlowBlock{Type="if",Id=index});b.AppendLine("if("+condition+"){ ");return true;
        }
    }
}
