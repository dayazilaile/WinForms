using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private sealed class BranchListItem
        {
            public AutoStepRange Range;public string Text;
            public override string ToString(){return Text;}
        }
        private List<AutoStep> EditConditionBlock(IList<AutoStep> source,AutoStepRange range)
        {
            int end=range.Start+range.Count-1,split=range.ElseIndex<0?end:range.ElseIndex;
            var yes=AutomationBlockService.CopyRange(source,range.Start+1,split-range.Start-1);
            var no=range.ElseIndex<0?new List<AutoStep>():AutomationBlockService.CopyRange(source,split+1,end-split-1);
            return EditStepBlock(AutomationBlockService.Copy(source[range.Start]),yes,no,range.ElseIndex>=0,true);
        }
        private GroupBox ConditionBranchPanel(string title,List<AutoStep> actions)
        {
            var group=new GroupBox{Text=title,Dock=DockStyle.Fill,Padding=new Padding(10),Margin=new Padding(0,0,12,0)};
            var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true,AccessibleName=title,IntegralHeight=false,Font=Ui};
            var hint=new Label{Dock=DockStyle.Top,Height=28,Text="按顺序执行，可添加多个动作；留空表示不执行。",AutoEllipsis=true};
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,WrapContents=true,Margin=Padding.Empty};
            group.Controls.Add(list);group.Controls.Add(hint);group.Controls.Add(buttons);list.BringToFront();
            Action<int> render=delegate(int selected){list.Items.Clear();for(int i=0;i<actions.Count;){var range=AutomationBlockService.Range(actions,i);list.Items.Add(new BranchListItem{Range=range,Text=(list.Items.Count+1)+". "+actions[i]+(range.Count>1?"（完整块，共 "+range.Count+" 行）":"")});i+=range.Count;}if(list.Items.Count>0)list.SelectedIndex=Math.Max(0,Math.Min(selected,list.Items.Count-1));};
            Action edit=delegate{
                var item=list.SelectedItem as BranchListItem;if(item==null)return;var range=item.Range;
                if(actions[range.Start].Type=="重试开始"){MessageBox.Show(group.FindForm(),"此重试块已完整保留，请在主流程中编辑块内动作。","编辑重试块");return;}
                var updated=AutomationBlockService.IsCondition(actions[range.Start].Type)?EditConditionBlock(actions,range):EditStepBlock(AutomationBlockService.Copy(actions[range.Start]),new List<AutoStep>(),new List<AutoStep>(),true,true);
                if(updated!=null){int index=list.SelectedIndex;actions.RemoveRange(range.Start,range.Count);actions.InsertRange(range.Start,updated);render(index);}
            };
            Action<string,EventHandler> addButton=delegate(string caption,EventHandler click){var button=FormButton(caption,0,0,78,click);button.Margin=new Padding(0,4,5,0);buttons.Controls.Add(button);};
            addButton("添加动作",delegate{var added=EditStepBlock(null,new List<AutoStep>(),new List<AutoStep>(),true,true);if(added!=null){actions.AddRange(added);render(Int32.MaxValue);}});
            addButton("编辑",delegate{edit();});
            addButton("删除",delegate{var item=list.SelectedItem as BranchListItem;if(item!=null){int index=list.SelectedIndex;actions.RemoveRange(item.Range.Start,item.Range.Count);render(index);}});
            addButton("上移",delegate{int index=list.SelectedIndex;if(index<=0)return;var item=(BranchListItem)list.Items[index];var previous=(BranchListItem)list.Items[index-1];AutomationBlockService.Move(actions,item.Range.Start,previous.Range.Start);render(index-1);});
            addButton("下移",delegate{int index=list.SelectedIndex;if(index<0||index>=list.Items.Count-1)return;var item=(BranchListItem)list.Items[index];var next=(BranchListItem)list.Items[index+1];AutomationBlockService.Move(actions,item.Range.Start,next.Range.Start+next.Range.Count);render(index+1);});
            list.DoubleClick+=delegate{edit();};render(0);return group;
        }
    }
}
