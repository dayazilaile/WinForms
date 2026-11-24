using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace WinForms
{
    public sealed partial class MainForm
    {
        private void BuildTrialOverview()
        {
            FlowLayoutPanel bar;Label state;var root=Workspace("新增功能总览","新功能先放本组，显示加入版本；实测确认后再整理到正式目录",out bar,out state);
            var open=WorkspaceButton(bar,"打开所选功能",true);var grid=Table(new[]{"功能","加入版本","状态","用途 / 本版变化"});grid.Name="trialFeaturesGrid";grid.MultiSelect=false;SetColumnWidths(grid,new[]{220,100,155,480});grid.Columns[3].AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill;
            foreach(var entry in NavigationCatalog.All)if(entry.Trial&&entry.Key!="新增功能总览"){
                int i=grid.Rows.Add(entry.Key,entry.Since.Length==0?"历史试用":"v"+entry.Since,entry.Key=="软件清单清理BAT"?"已有功能的新入口":"待试用",entry.Note.Length==0?"历史待试用功能，尚未收到保留确认。":entry.Note);grid.Rows[i].Tag=entry.Key;
            }
            Action go=delegate{if(grid.CurrentRow!=null)SelectNavigationPage((string)grid.CurrentRow.Tag);};open.Click+=delegate{go();};grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0)go();};root.Controls["pageBody"].Controls.Add(grid);grid.BringToFront();state.Text="显示 "+grid.Rows.Count+" 个入口；已有功能的新入口会单独标注，不自动执行功能。";
        }
        private void BuildSoftwareCleanupGuide()
        {
            FlowLayoutPanel bar;Label state;var root=Workspace("软件清单清理 BAT · 入口整理待试用","v0.6.4 新增直达向导；批量清理能力为已有功能，不会在此电脑自动卸载",out bar,out state);
            var manage=WorkspaceButton(bar,"建立 / 编辑标准",true);var import=WorkspaceButton(bar,"导入标准",false);var refresh=WorkspaceButton(bar,"刷新标准状态",false);var generate=WorkspaceButton(bar,"生成清理 BAT",true);
            generate.Name="generateSoftwareCleanupBat";var info=ReadBox();info.Name="softwareCleanupGuide";root.Controls["pageBody"].Controls.Add(info);info.BringToFront();List<SoftwareBaselineEntry> rules=null;
            Action load=delegate{rules=null;generate.Enabled=false;var text=new StringBuilder();text.AppendLine("1. 在配置正确的电脑建立应保留的软件标准，或导入已有标准。\r\n2. 点击“生成清理 BAT”，选择“确认后批量卸载”或“自动执行”。\r\n3. 保存单个 BAT，带到要清理的电脑运行；脚本会重新核对那台电脑的软件。\r\n");text.AppendLine("原入口：系统与硬件 → 软件环境检查 → 标准清单 ▾ → 导出批量卸载脚本。\r\n本向导复用原生成器，不是 BAT 鼠标自动化流程，也不在教师机执行卸载。\r\n");
                try{var saved=softwareBaselineReader();if(saved!=null){rules=SoftwareBaselineService.Checked(saved);generate.Enabled=true;text.AppendLine("已读取保留标准："+rules.Count+" 项。");}else text.AppendLine("尚未建立标准。请先点“建立 / 编辑标准”，从本机建立或导入正确清单。");state.Text=rules==null?"未建立标准，不会把全部软件当成多出，也不能生成清理脚本。":"已读取 "+rules.Count+" 项保留标准；生成前仍会选择模式并确认保存。";}
                catch(Exception ex){text.AppendLine("标准读取失败："+ex.Message);state.Text="未能读取有效标准，已禁用生成；原文件不会被覆盖。";}
                text.AppendLine("\r\n默认标准位置："+SoftwareBaselineService.DefaultPath);text.AppendLine("结果只弹窗，不额外留清理报告或脚本日志；保护项、驱动、系统/安全组件等仍按原规则跳过。");info.Text=text.ToString();};
            manage.Click+=delegate{SelectNavigationPage("软件环境检查");};refresh.Click+=delegate{load();};generate.Click+=delegate{if(rules!=null)ExportSoftwareCleanup(rules);};
            import.Click+=delegate{try{string text=softwareBaselineImportReader==null?ReadToolFile(this):softwareBaselineImportReader();if(text==null)return;var parsed=SoftwareBaselineService.Parse(text);softwareBaselineWriter(parsed);load();}catch(Exception ex){state.Text="导入失败，原标准保留："+ex.Message;}};load();
        }
    }
}
