using System;
using System.Collections.Generic;
namespace ITDesktopToolkit
{
    public sealed class AutoStepRange { public int Start,Count,ElseIndex=-1; }
    public static class AutomationBlockService
    {
        public static bool IsCondition(string type){return type=="如果文件存在"||type=="如果网络可达"||type=="如果系统版本";}
        public static AutoStep Copy(AutoStep step){if(step==null)throw new ArgumentException("动作不能为空。");return new AutoStep{Type=step.Type,A=step.A,B=step.B,C=step.C};}
        public static List<AutoStep> CopyRange(IList<AutoStep> source,int start,int count)
        {var result=new List<AutoStep>();for(int i=start;i<start+count;i++)result.Add(Copy(source[i]));return result;}
        public static AutoStepRange Range(IList<AutoStep> source,int index)
        {
            if(index<0||index>=source.Count)throw new ArgumentException("请选择有效动作。");
            // A branch delimiter belongs to its matching condition header.
            if(source[index].Type=="否则"||source[index].Type=="结束条件"||source[index].Type=="重试结束")
            {
                var parents=new Stack<int>();for(int i=0;i<index;i++)
                {
                    string type=source[i].Type;if(IsCondition(type)||type=="重试开始")parents.Push(i);
                    else if(type=="结束条件"||type=="重试结束"){if(parents.Count==0||((type=="结束条件")!=IsCondition(source[parents.Peek()].Type)))throw new ArgumentException("旧流程的开始/结束动作不匹配。");parents.Pop();}
                }
                if(parents.Count==0||((source[index].Type!="重试结束")!=IsCondition(source[parents.Peek()].Type)))throw new ArgumentException("此分支没有匹配的开始动作，请先补齐流程。");index=parents.Peek();
            }
            var range=new AutoStepRange{Start=index,Count=1};string first=source[index].Type;
            if(!IsCondition(first)&&first!="重试开始")return range;
            var stack=new Stack<string>();stack.Push(first=="重试开始"?"retry":"if");
            for(int i=index+1;i<source.Count;i++)
            {
                string type=source[i].Type;
                if(IsCondition(type)||type=="重试开始")stack.Push(type=="重试开始"?"retry":"if");
                else if(type=="否则")
                {if(stack.Peek()!="if")throw new ArgumentException("否则与条件不匹配。");if(stack.Count==1){if(range.ElseIndex>=0)throw new ArgumentException("同一条件存在多个否则。");range.ElseIndex=i;}}
                else if(type=="结束条件"||type=="重试结束")
                {
                    if(stack.Pop()!=(type=="结束条件"?"if":"retry"))throw new ArgumentException("条件或重试的结束动作不匹配。");
                    if(stack.Count==0){range.Count=i-index+1;return range;}
                }
            }
            throw new ArgumentException("此条件或重试块缺少结束动作，请先补齐旧流程。");
        }
        public static List<AutoStep> Compose(AutoStep condition,IList<AutoStep> yes,IList<AutoStep> no,bool includeElse)
        {
            if(!IsCondition(condition.Type))throw new ArgumentException("请选择条件动作。");
            // Validate each branch separately so a stray delimiter cannot escape its branch.
            var left=CopyRange(yes,0,yes.Count);var right=CopyRange(no,0,no.Count);
            AutomationService.ValidateFlow(left);AutomationService.ValidateFlow(right);
            var result=new List<AutoStep>{Copy(condition)};result.AddRange(left);
            if(includeElse||right.Count>0){result.Add(new AutoStep{Type="否则",A="",B="",C=""});result.AddRange(right);}
            result.Add(new AutoStep{Type="结束条件",A="",B="",C=""});
            AutomationService.MakeScript(result,BatInputEngine.SendInput,0,false);return result;
        }
        public static int Move(List<AutoStep> source,int index,int target)
        {
            if(target<0||target>source.Count)throw new ArgumentException("移动位置无效。");var range=Range(source,index);
            if(target>=range.Start&&target<=range.Start+range.Count)return range.Start;
            var block=source.GetRange(range.Start,range.Count);int destination=target>range.Start?target-range.Count:target;
            source.RemoveRange(range.Start,range.Count);source.InsertRange(destination,block);return destination;
        }
    }
}
