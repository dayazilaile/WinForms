using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ITDesktopToolkit
{
    public sealed partial class MainForm
    {
        private List<AutoStep> autoSteps=new List<AutoStep>();
        // Local-only drag token. It never leaves the editor or the exported BAT.
        private sealed class AutoStepDragToken
        {
            public DataGridView Source;
            public int Index;
            public AutoStepDragToken(DataGridView source,int index) { Source=source; Index=index; }
        }
        private void BuildAutomation()
        {
            FlowLayoutPanel bar;
            Panel page=Page("BAT 自动化", "可拖动排序的动作编辑器 · 两种 BAT 生成方式 · 可选 UAC 提权、启动延时、隐藏窗口及流程末尾自删除", out bar);
            bar.Height=105; bar.WrapContents=true; bar.AutoScroll=true;

            // Actions are the focus. Execution/export settings are stored separately.
            Panel optionsPanel=new Panel { Dock=DockStyle.Top,Height=157,BackColor=Color.White };
            Label optionsTitle=new Label { Text="脚本生成选项（与动作列表独立）",Left=13,Top=8,Width=380,Height=24,
                ForeColor=Navy,Font=new Font("DengXian",11F,FontStyle.Bold) };
            Label engineLabel=new Label { Text="生成方式",Left=15,Top=40,Width=78,Height=28,TextAlign=ContentAlignment.MiddleLeft };
            ComboBox engineChoice=new ComboBox { Left=93,Top=40,Width=235,Height=30,DropDownStyle=ComboBoxStyle.DropDownList };
            engineChoice.Items.Add("方案 A：新版 SendInput");
            engineChoice.Items.Add("方案 B：传统兼容（免切英文）");
            engineChoice.SelectedIndex=0;
            CheckBox hideConsole=new CheckBox { Text="隐藏运行窗口",Left=353,Top=39,Width=156,Height=31,Checked=false,ForeColor=Navy };
            CheckBox deleteOnSuccess=new CheckBox { Text="执行到末尾后删除 BAT（成功或失败）",Left=525,Top=39,Width=265,Height=31,Checked=true,ForeColor=Navy };
            Label delayLabel=new Label { Text="启动前延时",Left=15,Top=80,Width=90,Height=28,TextAlign=ContentAlignment.MiddleLeft };
            NumericUpDown startupDelay=new NumericUpDown { Left=106,Top=81,Width=86,Height=27,Minimum=0,Maximum=3600,Value=3 };
            Label delayUnit=new Label { Text="秒",Left=199,Top=79,Width=40,Height=29,TextAlign=ContentAlignment.MiddleLeft };
            CheckBox runAsAdmin=new CheckBox { Text="使用管理员身份运行（自动弹出 UAC）",Left=247,Top=79,Width=420,Height=31,Checked=true,ForeColor=Navy };
            CheckBox diagnosticMode=new CheckBox { Text="启用跨机房环境诊断（隐藏运行也有效）",Left=247,Top=115,Width=470,Height=31,Checked=true,ForeColor=Navy };
            optionsPanel.Controls.AddRange(new Control[]{optionsTitle,engineLabel,engineChoice,hideConsole,deleteOnSuccess,delayLabel,startupDelay,delayUnit,runAsAdmin,diagnosticMode});

            SplitContainer split=new SplitContainer { Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=230,Panel1MinSize=140,Panel2MinSize=90 };
            DataGridView steps=Grid();
            // Drag handle is deliberately restricted to the FIRST column.
            // Dragging parameter cells will not accidentally reorder actions.
            steps.AllowDrop=true;
            AddColumn(steps,"handle","拖动",55,false);
            AddColumn(steps,"no","序号",55,false);AddColumn(steps,"action","动作",130,false);
            AddColumn(steps,"a","参数1",255,true);AddColumn(steps,"b","参数2",170,false);AddColumn(steps,"c","参数3",150,false);
            foreach(DataGridViewColumn column in steps.Columns) column.SortMode=DataGridViewColumnSortMode.NotSortable;
            steps.Columns["handle"].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
            steps.Columns["handle"].DefaultCellStyle.Font=new Font("Segoe UI Symbol",14F,FontStyle.Bold);
            steps.Columns["handle"].ToolTipText="按住 ☰，拖动到目标位置即可调整动作顺序";
            split.Panel1.Controls.Add(steps);
            RichTextBox preview=ReadBox();split.Panel2.Controls.Add(preview);
            Panel editor=new Panel { Dock=DockStyle.Fill,BackColor=Color.White };
            editor.Controls.Add(split);
            editor.Controls.Add(optionsPanel);
            page.Controls["pageBody"].Controls.Add(ContentCard(editor));

            Func<AutoWorkflowSettings> readOptions=delegate {
                return new AutoWorkflowSettings {
                    Engine=engineChoice.SelectedIndex==1?BatInputEngine.Legacy:BatInputEngine.SendInput,
                    HideConsole=hideConsole.Checked,
                    DeleteOnSuccess=deleteOnSuccess.Checked,
                    RunAsAdministrator=runAsAdmin.Checked,
                    DiagnosticMode=diagnosticMode.Checked,
                    StartupDelaySeconds=Decimal.ToInt32(startupDelay.Value)
                };
            };
            Action show=delegate {
                bool hasFormat=AutomationService.HasFormatStep(autoSteps);
                if(hasFormat && hideConsole.Checked) hideConsole.Checked=false;
                hideConsole.Enabled=!hasFormat;
                deleteOnSuccess.Enabled=true;
                steps.Rows.Clear();
                for(int i=0;i<autoSteps.Count;i++){
                    AutoStep x=autoSteps[i];steps.Rows.Add("☰",i+1,x.Type,x.A,x.B,x.C);
                }
                StringBuilder b=new StringBuilder("【当前动作流程】\r\n");
                b.AppendLine("排序：按住 ☰ 拖动。复用：选中一行后点“复制动作”或按 Ctrl+D，普通动作副本插在下一行，条件/重试块整块复制、拖动与删除；双击条件可编辑两个分支。");
                if(autoSteps.Count==0)b.AppendLine("尚无动作，请点击『添加动作』。");
                for(int i=0;i<autoSteps.Count;i++)b.AppendLine((i+1)+". "+autoSteps[i]);
                b.AppendLine("\r\n【脚本生成选项】");
                b.AppendLine("BAT 引擎："+(engineChoice.SelectedIndex==1?"传统兼容 mouse_event / keybd_event / 剪贴板文字":"新版 SendInput"));
                if(engineChoice.SelectedIndex==1) b.AppendLine("传统模式的文字输入将临时使用剪贴板粘贴，尽量恢复原有内容；需要目标程序允许粘贴。");
                b.AppendLine("启动前延时："+startupDelay.Value+" 秒；动作间可插入『等待毫秒』或『随机等待』。");
                b.AppendLine("运行窗口："+(hideConsole.Checked?"隐藏 PowerShell（启动 BAT 可能短暂闪现，不生成运行日志）":"显示运行控制台"));
                b.AppendLine("自删除："+(deleteOnSuccess.Checked?"流程到末尾即尝试删除 BAT（不区分成功失败）":"关闭，保留 BAT 文件"));
                b.AppendLine("执行权限："+(runAsAdmin.Checked?"启动 BAT 时自动申请管理员权限（需确认 UAC）":"沿用当前用户权限，不主动提权"));
                b.AppendLine("环境诊断："+(diagnosticMode.Checked?"开启，打印分辨率、DPI、鼠标限制和前台窗口，不额外写文件":"关闭"));
                b.AppendLine("两种模式都是单个 BAT，目标电脑仅需系统自带 PowerShell，不需要安装工具箱。");
                if(autoSteps.Exists(delegate(AutoStep x){return x!=null && x.Type=="删除指定路径文件";}))
                    b.AppendLine("指定文件删除：选择完整绝对路径，支持跨盘；不会进入回收站，也不会匹配文件夹或通配符。");
                if(autoSteps.Exists(delegate(AutoStep x){return x!=null && x.Type=="清空当前用户回收站";}))
                    b.AppendLine("清空回收站：永久删除 BAT 实际运行账户在所有驱动器的回收站内容；提权使用其他管理员账户时，清理的是该账户。");
                if(autoSteps.Exists(delegate(AutoStep x){return x!=null && x.Type=="删除桌面指定文件";}))
                    b.AppendLine("桌面文件清理：精确名称，不使用通配符，不清理目录。当前用户桌面指 BAT 实际运行账户；以其他管理员账户提权时需注意账户不同。公共桌面影响所有用户，删除不可从回收站恢复。");
                b.AppendLine("等待窗口默认最多 120 秒；条件判断超过 120 秒或报错即停止后续动作，按末尾删除开关清理。明确不成立仍走否则分支。");
                b.AppendLine("等待窗口只判断出现，不会自动聚焦；要发送回车前请添加『激活窗口』或点击目标输入框。");
                b.AppendLine("鼠标坐标和窗口焦点依赖目标电脑 DPI、权限、分辨率和交互会话，请先测试。");
                b.AppendLine("网络操作需要管理员权限，执行前备份当前 IPv4 配置。静态 IP 批量应用须避免地址冲突。");
                b.AppendLine(hasFormat ? "【高风险】包含格式化卷：不能隐藏运行；BAT 可选择在末尾自删，请核实目标机盘符。" :
                    "脚本到达收尾阶段时不论此前动作成功或失败都尝试自删除；系统断电或强制终止除外。");
                preview.Text=b.ToString();
            };
            engineChoice.SelectedIndexChanged+=delegate {show();};
            hideConsole.CheckedChanged+=delegate {show();};
            deleteOnSuccess.CheckedChanged+=delegate {show();};
            runAsAdmin.CheckedChanged+=delegate {show();};
            diagnosticMode.CheckedChanged+=delegate {show();};
            startupDelay.ValueChanged+=delegate {show();};
            // Drag-to-reorder: the blue line shows the INSERTION boundary, not
            // just the hovered row. Drop updates the same autoSteps list used by
            // Save/Load/Export; no change to the BAT execution engine.
            int pressedRow=-1;
            int insertionIndex=-1;
            Point pressedPoint=Point.Empty;
            bool dragging=false;
            steps.CellMouseEnter+=delegate(object sender,DataGridViewCellEventArgs e) {
                steps.Cursor=e.ColumnIndex==0 && e.RowIndex>=0?Cursors.SizeAll:Cursors.Default;
            };
            steps.CellMouseLeave+=delegate { if(!dragging) steps.Cursor=Cursors.Default; };
            steps.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e) {
                pressedRow=e.Button==MouseButtons.Left && e.ColumnIndex==0 && e.RowIndex>=0?e.RowIndex:-1;
                pressedPoint=steps.PointToClient(Cursor.Position);
            };
            steps.MouseMove+=delegate(object sender,MouseEventArgs e) {
                if(pressedRow<0 || dragging || (e.Button & MouseButtons.Left)!=MouseButtons.Left) return;
                Size threshold=SystemInformation.DragSize;
                if(Math.Abs(e.X-pressedPoint.X)<threshold.Width/2 && Math.Abs(e.Y-pressedPoint.Y)<threshold.Height/2) return;
                AutoStepDragToken token=new AutoStepDragToken(steps,pressedRow);
                pressedRow=-1;
                dragging=true;
                try { steps.DoDragDrop(token,DragDropEffects.Move); }
                finally {
                    dragging=false;
                    insertionIndex=-1;
                    steps.Cursor=Cursors.Default;
                    steps.Invalidate();
                }
            };
            steps.MouseUp+=delegate { pressedRow=-1; };
            steps.DragEnter+=delegate(object sender,DragEventArgs e) {
                AutoStepDragToken token=e.Data.GetData(typeof(AutoStepDragToken)) as AutoStepDragToken;
                e.Effect=token!=null && Object.ReferenceEquals(token.Source,steps)?DragDropEffects.Move:DragDropEffects.None;
            };
            steps.DragOver+=delegate(object sender,DragEventArgs e) {
                AutoStepDragToken token=e.Data.GetData(typeof(AutoStepDragToken)) as AutoStepDragToken;
                if(token==null || !Object.ReferenceEquals(token.Source,steps)) {
                    e.Effect=DragDropEffects.None;
                    insertionIndex=-1;
                    steps.Invalidate();
                    return;
                }
                e.Effect=DragDropEffects.Move;
                Point at=steps.PointToClient(new Point(e.X,e.Y));
                DataGridView.HitTestInfo hit=steps.HitTest(at.X,at.Y);
                int next=-1;
                if(hit.RowIndex>=0) {
                    Rectangle row=steps.GetRowDisplayRectangle(hit.RowIndex,false);
                    next=hit.RowIndex+(at.Y>=row.Top+row.Height/2?1:0);
                } else if(steps.Rows.Count==0) next=0;
                else if(at.Y<steps.ColumnHeadersHeight) next=Math.Max(0,steps.FirstDisplayedScrollingRowIndex);
                else if(at.Y<steps.ClientSize.Height) {
                    int lastVisible=-1;
                    int firstVisible=Math.Max(0,steps.FirstDisplayedScrollingRowIndex);
                    for(int i=firstVisible;i<steps.Rows.Count;i++) {
                        if(!steps.Rows[i].Displayed) break;
                        lastVisible=i;
                    }
                    if(lastVisible>=0 && at.Y>=steps.GetRowDisplayRectangle(lastVisible,false).Bottom)
                        next=lastVisible+1;
                }
                if(next<0) {
                    e.Effect=DragDropEffects.None;
                }
                if(next!=insertionIndex) { insertionIndex=next; steps.Invalidate(); }
                // Autoscroll while moving the handle near the top/bottom.
                if(steps.Rows.Count>0 && at.Y>steps.ColumnHeadersHeight && at.Y<steps.ClientSize.Height) {
                    try {
                        int first=steps.FirstDisplayedScrollingRowIndex;
                        if(first>=0) {
                            if(at.Y<steps.ColumnHeadersHeight+22 && first>0) steps.FirstDisplayedScrollingRowIndex=first-1;
                            else if(at.Y>steps.ClientSize.Height-25 && first<steps.Rows.Count-1) steps.FirstDisplayedScrollingRowIndex=first+1;
                        }
                    } catch(InvalidOperationException) { }
                }
            };
            steps.DragLeave+=delegate {
                insertionIndex=-1;
                steps.Invalidate();
            };
            steps.DragDrop+=delegate(object sender,DragEventArgs e) {
                AutoStepDragToken token=e.Data.GetData(typeof(AutoStepDragToken)) as AutoStepDragToken;
                int target=insertionIndex;
                insertionIndex=-1;
                steps.Invalidate();
                if(token==null || !Object.ReferenceEquals(token.Source,steps) ||
                   token.Index<0 || token.Index>=autoSteps.Count || target<0 || target>autoSteps.Count) return;
                try{int newIndex=AutomationBlockService.Move(autoSteps,token.Index,target);show();SelectAutoStepRow(steps,newIndex);}catch(Exception ex){MessageBox.Show(this,ex.Message,"移动动作");}
            };
            steps.Paint+=delegate(object sender,PaintEventArgs e) {
                if(insertionIndex<0 || steps.Rows.Count==0) return;
                Rectangle rect;
                if(insertionIndex==steps.Rows.Count) rect=steps.GetRowDisplayRectangle(steps.Rows.Count-1,false);
                else rect=steps.GetRowDisplayRectangle(insertionIndex,false);
                int y=insertionIndex==steps.Rows.Count?rect.Bottom:rect.Top;
                if(y<steps.ColumnHeadersHeight || y>steps.ClientSize.Height) return;
                using(Pen pen=new Pen(Color.FromArgb(36,124,220),3F))
                    e.Graphics.DrawLine(pen,0,y,steps.ClientSize.Width-2,y);
            };
            Action(bar,"添加动作",delegate {var added=EditStep(null);if(added!=null){autoSteps.AddRange(added);show();}},true);
            Action editSelected=delegate{int i=steps.CurrentRow==null?-1:steps.CurrentRow.Index;if(i<0)return;try{var range=AutomationBlockService.Range(autoSteps,i);List<AutoStep> edited;if(AutomationBlockService.IsCondition(autoSteps[range.Start].Type))edited=EditConditionBlock(autoSteps,range);else{range=new AutoStepRange{Start=i,Count=1};edited=EditStep(autoSteps[i]);}if(edited!=null){autoSteps.RemoveRange(range.Start,range.Count);autoSteps.InsertRange(range.Start,edited);show();SelectAutoStepRow(steps,range.Start);}}catch(Exception ex){MessageBox.Show(this,ex.Message,"编辑动作");}};
            Action(bar,"编辑动作",delegate{editSelected();},false);
            Action(bar,"删除动作",delegate {int i=steps.CurrentRow==null?-1:steps.CurrentRow.Index;if(i<0)return;try{var range=AutomationBlockService.Range(autoSteps,i);if(ConfirmAction("删除动作","删除第 "+(range.Start+1)+" 行"+(range.Count>1?"及其完整条件/重试块（共 "+range.Count+" 行）":"")+"？")){autoSteps.RemoveRange(range.Start,range.Count);show();}}catch(Exception ex){MessageBox.Show(this,ex.Message);}},false);
            Action duplicate=delegate {
                int i=steps.CurrentRow==null?-1:steps.CurrentRow.Index;if(i<0||i>=autoSteps.Count)return;
                try{var range=AutomationBlockService.Range(autoSteps,i);var copy=AutomationBlockService.CopyRange(autoSteps,range.Start,range.Count);int next=range.Start+range.Count;autoSteps.InsertRange(next,copy);show();SelectAutoStepRow(steps,next);}catch(Exception ex){MessageBox.Show(this,ex.Message,"复制动作");}
            };
            Action(bar,"复制动作",delegate{duplicate();},false);
            steps.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.D){duplicate();e.Handled=true;e.SuppressKeyPress=true;}};
            Action(bar,"保存流程",delegate {using(SaveFileDialog d=new SaveFileDialog()){d.InitialDirectory=AppDomain.CurrentDomain.BaseDirectory;
                d.Filter="自动化工作流 (*.workflow.txt)|*.workflow.txt";d.FileName="我的自动化流程.workflow.txt";
                if(d.ShowDialog(this)==DialogResult.OK){try{AutomationService.Save(d.FileName,autoSteps,readOptions());MessageBox.Show(this,"流程和生成选项均已保存。","保存成功");}catch(Exception ex){MessageBox.Show(this,ex.Message);}}
            }},false);
            Action(bar,"打开流程",delegate {using(OpenFileDialog d=new OpenFileDialog()){
                d.Filter="自动化工作流 (*.workflow.txt)|*.workflow.txt";
                if(d.ShowDialog(this)==DialogResult.OK){try{
                    AutoWorkflowSettings settings;autoSteps=AutomationService.Load(d.FileName,out settings);
                    engineChoice.SelectedIndex=settings.Engine==BatInputEngine.Legacy?1:0;
                    startupDelay.Value=settings.StartupDelaySeconds;
                    hideConsole.Checked=settings.HideConsole;
                    deleteOnSuccess.Checked=settings.DeleteOnSuccess;
                    runAsAdmin.Checked=settings.RunAsAdministrator;
                    diagnosticMode.Checked=settings.DiagnosticMode;
                    show();
                }catch(Exception ex){MessageBox.Show(this,"加载失败："+ex.Message);}}
            }},false);
            Action(bar,"生成 BAT",delegate {using(SaveFileDialog d=new SaveFileDialog()){d.InitialDirectory=AppDomain.CurrentDomain.BaseDirectory;
                d.Filter="BAT 批处理 (*.bat)|*.bat";
                d.FileName=engineChoice.SelectedIndex==1?"桌面自动化_传统兼容.bat":"桌面自动化_SendInput.bat";
                if(d.ShowDialog(this)==DialogResult.OK){try{
                    AutoWorkflowSettings options=readOptions();
                    AutomationService.ExportBat(d.FileName,autoSteps,options);
                    MessageBox.Show(this,"已生成独立 BAT：\r\n"+d.FileName+"\r\n\r\n模式："+(options.Engine==BatInputEngine.Legacy?"传统兼容":"新版 SendInput")+
                        "\r\n启动延时："+options.StartupDelaySeconds+" 秒"+
                        "\r\n隐藏运行："+(options.HideConsole?"是，不生成运行日志":"否")+
                        "\r\n管理员模式："+(options.RunAsAdministrator?"是，启动时弹出 UAC":"否")+
                        "\r\n环境诊断："+(options.DiagnosticMode?"是，仅在控制台显示":"否")+
                        "\r\n完成后自删除："+(options.DeleteOnSuccess?"执行到末尾":"否")+
                        "\r\n目标电脑无需安装本工具箱，请先测试。","生成成功");
                }catch(Exception ex){MessageBox.Show(this,"生成失败："+ex.Message);}}
            }},true);
            Action(bar,"清空全部",delegate {if(ConfirmAction("清空流程","清空全部动作？（生成选项保持不变）")){autoSteps.Clear();show();}},false);
            steps.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0 && e.ColumnIndex!=0){editSelected();}};
            show();
        }
        private void SelectAutoStepRow(DataGridView grid,int index)
        {
            if(index<0 || index>=grid.Rows.Count)return;
            grid.ClearSelection();
            grid.CurrentCell=grid.Rows[index].Cells[1];
            grid.Rows[index].Selected=true;
            try { grid.FirstDisplayedScrollingRowIndex=index; } catch(InvalidOperationException) { }
        }
        private Func<Point> mousePositionReader=MouseCoordinateService.CurrentPhysicalPosition;
        private Action<Point> mousePositionMover=MouseCoordinateService.MovePhysicalPosition;
        private List<AutoStep> EditStep(AutoStep current){return EditStepBlock(current,new List<AutoStep>(),new List<AutoStep>(),true,false);}
        private List<AutoStep> EditStepBlock(AutoStep current,List<AutoStep> yes,List<AutoStep> no,bool includeElse,bool branchOnly)
        {
            using(Form f=NewDialog(current==null?"添加自动化动作":"编辑自动化动作",730,458))
            {
                ComboBox type=new ComboBox{Left=170,Top=25,Width=530,DropDownStyle=ComboBoxStyle.DropDownList};
                foreach(string actionType in AutomationService.Types)if(!branchOnly||(actionType!="否则"&&actionType!="结束条件"&&actionType!="重试开始"&&actionType!="重试结束"))type.Items.Add(actionType);
                if(current!=null && current.Type=="删除桌面指定文件")type.Items.Add("删除桌面指定文件"); // 旧工作流只读兼容
                type.SelectedItem=current==null?"移动鼠标":current.Type;
                TextBox a=FormText(current==null?"":current.A,170,78,530);
                TextBox b=FormText(current==null?"":current.B,170,122,530);
                TextBox c=FormText(current==null?"":current.C,170,166,530);
                ComboBox desktopScope=new ComboBox{Left=170,Top=122,Width=530,Height=30,DropDownStyle=ComboBoxStyle.DropDownList};
                desktopScope.Items.AddRange(new object[]{"当前用户桌面","公共桌面","两者"});
                string savedScope=current==null?"":(current.B??"");
                desktopScope.SelectedItem=desktopScope.Items.Contains(savedScope)?savedScope:"当前用户桌面";
                var ipModeLabel=FormLabel("成功后",25,208,130);
                var ipMode=new ComboBox{Left=170,Top=208,Width=530,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="随机IPv4成功后"};ipMode.Items.AddRange(new object[]{"继续流程","结束脚本"});ipMode.SelectedIndex=0;
                bool randomIpInitialized=current!=null&&current.Type=="随机设置 IPv4";
                if(randomIpInitialized){var stored=(current.C??"").Split('|');c.Text=stored[0];if(stored.Length==2&&stored[1]=="结束脚本")ipMode.SelectedIndex=1;}
                Label parameterALabel=FormLabel("参数 1",25,77,130);
                Label parameterBLabel=FormLabel("参数 2",25,120,130);
                Label parameterCLabel=FormLabel("参数 3",25,164,130);
                Label guide=new Label{Left=25,Top=216,Width=675,Height=48,ForeColor=Navy};
                type.SelectedIndexChanged+=delegate {guide.Text=AutomationService.Guide(type.Text);};
                Button browse=FormButton("选择 EXE/BAT 文件（自动补全路径）",170,274,300,delegate {
                    using(OpenFileDialog d=new OpenFileDialog()){
                        d.Filter=(type.Text=="删除指定路径文件"||type.Text=="如果文件存在"||type.Text=="检查文件存在")?"所有文件 (*.*)|*.*":"程序和批处理 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|全部文件 (*.*)|*.*";
                        d.CheckFileExists=true;d.CheckPathExists=true;d.RestoreDirectory=true;
                        d.Title=type.Text=="删除指定路径文件"?"选择自动化要删除的文件（不可恢复）":type.Text=="如果文件存在"?"选择用于存在判断的文件":type.Text=="检查文件存在"?"选择要检查的文件":"选择要运行的完整程序文件（不是只选文件夹）";
                        if(d.ShowDialog(f)==DialogResult.OK){a.Text=Path.GetFullPath(d.FileName);if(type.Text=="运行程序")c.Text=Path.GetDirectoryName(d.FileName);}

                    }
                });
                a.AccessibleName="参数 1";b.AccessibleName="参数 2";c.AccessibleName="参数 3";
                var captureState=new Label{Left=25,Top=322,Width=675,Height=55,ForeColor=Navy,Text="坐标使用屏幕物理像素，支持副屏负坐标；捕获后可测试移动核对。"};
                var captureTimer=new Timer{Interval=100};var captureClock=new System.Diagnostics.Stopwatch();bool testingMove=false;Point moveTarget=Point.Empty;
                Button capture=null,testMove=null;
                Action stopCapture=delegate{captureTimer.Stop();captureClock.Reset();if(!capture.IsDisposed){capture.Enabled=true;testMove.Enabled=true;capture.Text="3 秒后捕获鼠标坐标";testMove.Text="3 秒后测试移动";}};
                Action<bool> startCapture=delegate(bool move){
                    if(move){int x,y;if(!Int32.TryParse(a.Text,out x)||!Int32.TryParse(b.Text,out y)||x < -50000||x > 50000||y < -50000||y > 50000){captureState.Text="请先填写有效的 X、Y 坐标（-50000 至 50000）。";return;}moveTarget=new Point(x,y);}
                    testingMove=move;capture.Enabled=false;testMove.Enabled=false;captureClock.Restart();captureState.Text=move?"3 秒后移动到所填坐标，请查看目标位置。":"请把鼠标移到目标位置，3 秒后记录物理坐标。";captureTimer.Start();
                };
                capture=FormButton("3 秒后捕获鼠标坐标",170,274,250,delegate{startCapture(false);});
                testMove=FormButton("3 秒后测试移动",440,274,260,delegate{startCapture(true);});
                capture.Name="captureMouseCoordinates";testMove.Name="testMouseCoordinates";
                captureTimer.Tick+=delegate{
                    if(captureClock.ElapsedMilliseconds<3000){(testingMove?testMove:capture).Text="还有 "+Math.Ceiling((3000-captureClock.ElapsedMilliseconds)/1000.0)+" 秒";return;}
                    captureTimer.Stop();
                    try{if(testingMove){mousePositionMover(moveTarget);captureState.Text="已测试移动至物理坐标 X="+moveTarget.X+"，Y="+moveTarget.Y+"；请核对目标位置。";}else{Point pos=mousePositionReader();a.Text=pos.X.ToString(System.Globalization.CultureInfo.InvariantCulture);b.Text=pos.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);captureState.Text="已捕获物理坐标 X="+a.Text+"，Y="+b.Text+"，未按 Windows 缩放比例换算。";}}
                    catch(Exception ex){captureState.Text="坐标操作未完成："+ex.Message;}
                    finally{stopCapture();}
                };
                f.FormClosing+=delegate{stopCapture();};f.Disposed+=delegate{captureTimer.Dispose();};
                Button recordKey=FormButton("按一下键盘录入",170,274,190,delegate {
                    string recorded=KeyCaptureDialog.Capture(f);
                    if(!String.IsNullOrWhiteSpace(recorded)) a.Text=recorded;
                });
                Button selectWindow=FormButton("从已打开/最小化窗口选择",170,274,250,delegate {
                    WindowCatalogItem selected=WindowCatalog.Pick(f);
                    if(selected!=null){a.Text=selected.Title;b.Text=selected.ProcessName;}
                });
                FlowLayoutPanel delayShortcuts=new FlowLayoutPanel {Left=170,Top=274,Width=530,Height=44,WrapContents=false,AutoScroll=true};
                foreach(int seconds in new int[]{1,2,3,4,5,10}){
                    int value=seconds;
                    Button quick=new Button {Text=value+" 秒",Width=78,Height=33,Margin=new Padding(3,3,3,3),
                        FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Navy};
                    quick.Click+=delegate {a.Text=(value*1000).ToString();};
                    delayShortcuts.Controls.Add(quick);
                }
                bool randomInitialized=current!=null&&current.Type=="随机等待";
                List<AutoStep> result=null;
                var conditionError=new Label{Left=25,Top=616,Width=950,Height=34,ForeColor=Color.Firebrick,AutoEllipsis=true,Name="conditionError"};
                a.TextChanged+=delegate{conditionError.Text="";};b.TextChanged+=delegate{conditionError.Text="";};c.TextChanged+=delegate{conditionError.Text="";};
                var branchArea=new TableLayoutPanel{Left=25,Top=322,Width=950,Height=285,ColumnCount=2,RowCount=1,Anchor=AnchorStyles.Top|AnchorStyles.Left};branchArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));branchArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
                branchArea.Controls.Add(ConditionBranchPanel("条件成立时执行",yes),0,0);branchArea.Controls.Add(ConditionBranchPanel("条件不成立时执行",no),1,0);
                Button save=FormButton("确定添加 / 保存",505,389,195,delegate {
                    if(captureTimer.Enabled){captureState.Text="请等待坐标操作完成后再保存。";return;}
                    if(type.SelectedItem==null){MessageBox.Show(f,"请选择动作类型。");return;}
                    if(type.Text=="删除指定路径文件")
                    {
                        try {
                            string full=AutomationService.CheckDeleteFilePath(a.Text);
                            if(MessageBox.Show(f,"将删除指定的一个文件：\r\n"+full+"\r\n\r\n删除不会进入回收站，确定添加这个动作吗？",
                                "确认删除动作",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                            a.Text=full;
                        } catch(Exception ex){MessageBox.Show(f,ex.Message,"删除路径无效",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                    }
                    if(type.Text=="删除桌面指定文件")
                    {
                        try {
                            string fileName=AutomationService.CheckDesktopFileName(a.Text);
                            string scope=AutomationService.CheckDesktopScope(desktopScope.Text);
                            string msg="准备加入桌面文件清理动作：\r\n\r\n文件名："+fileName+"\r\n范围："+scope+
                                "\r\n\r\n仅匹配准确文件名，不删除文件夹；删除后不会进入回收站。确定继续？";
                            if(MessageBox.Show(f,msg,"确认添加桌面删除动作",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
                        } catch(Exception ex){MessageBox.Show(f,ex.Message,"文件名或桌面范围无效",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                    }
                    if(type.Text=="格式化指定卷")
                    {
                        string target=(a.Text??"").Trim().ToUpperInvariant();
                        if(target.EndsWith(":")||target.EndsWith(":\\")) target=target.Substring(0,1);
                        string msg="确定要添加格式化 "+target+": 盘的操作吗？\r\n\r\n此操作执行时将清除目标卷中的数据，请确认盘符正确。";
                        if(MessageBox.Show(f,msg,"确认添加格式化动作",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
                    }
                    if(type.Text=="清空当前用户回收站")
                    {
                        if(MessageBox.Show(f,"此动作执行时会永久清空 BAT 运行账户在所有驱动器的回收站。确定添加吗？",
                            "确认清空回收站动作",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                    }
                    var edited=new AutoStep{Type=type.Text,A=type.Text=="清空当前用户回收站"?"":a.Text,B=type.Text=="清空当前用户回收站"?"":type.Text=="删除桌面指定文件"?desktopScope.Text:b.Text,C=type.Text=="清空当前用户回收站"||type.Text=="删除桌面指定文件"?"":c.Text};
                    try{if(edited.Type=="随机设置 IPv4"){var config=AutomationService.RandomIpv4(edited.A,edited.B,edited.C+"|"+ipMode.Text);edited.A=config.Nic;edited.B=config.Network;edited.C=config.Gateway+"|"+(config.Finish?"结束脚本":"继续流程");}if(edited.Type=="随机等待"){var bounds=AutomationService.RandomWaitBounds(edited.A,edited.B);edited.A=bounds[0].ToString(System.Globalization.CultureInfo.InvariantCulture);edited.B=bounds[1].ToString(System.Globalization.CultureInfo.InvariantCulture);edited.C="";}if(AutomationBlockService.IsCondition(edited.Type)){edited.C="";if(edited.Type!="如果系统版本")edited.B="";edited.A=(edited.A??"").Trim();edited.B=(edited.B??"").Trim();if(yes.Count+no.Count==0)throw new ArgumentException("请至少在一个分支添加动作；无需执行的分支可留空。");result=AutomationBlockService.Compose(edited,yes,no,includeElse);}else{if(yes.Count+no.Count>0&&MessageBox.Show(f,"改为普通动作会移除当前条件的两个分支，是否继续？","更改动作类型",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;result=new List<AutoStep>{edited};}}catch(Exception ex){conditionError.Text=ex.Message;return;}
                    f.DialogResult=DialogResult.OK;f.Close();
                });
                f.Controls.AddRange(new Control[]{FormLabel("动作类型",25,23,130),type,parameterALabel,a,
                    parameterBLabel,b,desktopScope,parameterCLabel,c,ipModeLabel,ipMode,guide,browse,capture,testMove,captureState,recordKey,selectWindow,delayShortcuts,branchArea,conditionError,save});
                // 只有『运行程序』需要文件浏览；坐标捕获主要用于『移动鼠标』。
                Action refreshActionOptions=delegate {
                    browse.Visible=type.Text=="运行程序" || type.Text=="运行程序并等待" || type.Text=="如果文件存在" || type.Text=="检查文件存在" || type.Text=="删除指定路径文件";
                    browse.Text=type.Text=="删除指定路径文件"?"浏览选择要删除的文件":type.Text=="如果文件存在"?"浏览选择要判断的文件":type.Text=="检查文件存在"?"浏览选择要检查的文件":"选择 EXE/BAT 文件（自动补全路径）";
                    bool mouseAction=type.Text=="移动鼠标";capture.Visible=mouseAction;testMove.Visible=mouseAction;captureState.Visible=mouseAction;
                    if(!mouseAction){stopCapture();captureState.Text="坐标使用屏幕物理像素，支持副屏负坐标；捕获后可测试移动核对。";}
                    recordKey.Visible=type.Text=="按键/组合键";
                    selectWindow.Visible=type.Text=="激活窗口";
                    delayShortcuts.Visible=type.Text=="等待毫秒";
                    bool desktopAction=type.Text=="删除桌面指定文件";
                    bool noParameters=type.Text=="清空当前用户回收站";
                    desktopScope.Visible=desktopAction;
                    a.Visible=!noParameters;parameterALabel.Visible=!noParameters;
                    b.Visible=!desktopAction&&!noParameters;
                    c.Visible=!desktopAction&&!noParameters;
                    parameterCLabel.Visible=!desktopAction&&!noParameters;
                    parameterALabel.Text=desktopAction?"准确文件名":type.Text=="删除指定路径文件"?"文件完整路径":"参数 1";
                    parameterBLabel.Text=desktopAction?"桌面范围":"参数 2";
                    if(type.Text=="删除指定路径文件"){b.Visible=false;c.Visible=false;parameterBLabel.Visible=false;parameterCLabel.Visible=false;}else{parameterBLabel.Visible=!noParameters;}
                    bool condition=AutomationBlockService.IsCondition(type.Text),random=type.Text=="随机等待",randomIp=type.Text=="随机设置 IPv4";branchArea.Visible=condition;conditionError.Visible=condition||random||randomIp;ipMode.Visible=ipModeLabel.Visible=randomIp;parameterCLabel.Text="参数 3";c.AccessibleName="参数 3";conditionError.Text="";
                    f.MinimumSize=Size.Empty;f.MaximumSize=Size.Empty;f.ClientSize=condition?new Size(1000,620):new Size(730,458);f.MinimumSize=f.Size;f.MaximumSize=f.Size;save.Left=condition?780:505;save.Top=condition?566:389;guide.Top=condition?(type.Text=="如果系统版本"?170:122):216;browse.Top=condition?180:274;branchArea.Top=228;conditionError.Top=condition?522:340;conditionError.Width=condition?950:675;
                    guide.Height=randomIp?72:48;if(randomIp){guide.Top=253;parameterALabel.Text="网卡名称";parameterBLabel.Text="地址段 / CIDR";parameterCLabel.Text="IPv4 网关";a.AccessibleName=parameterALabel.Text;b.AccessibleName=parameterBLabel.Text;c.AccessibleName=parameterCLabel.Text;if(!randomIpInitialized){randomIpInitialized=true;a.Text="自动有线";b.Text="192.168.1.0/24";c.Text="192.168.1.254";ipMode.SelectedIndex=0;}}
                    if(condition){parameterALabel.Text=type.Text=="如果文件存在"?"文件完整路径":type.Text=="如果网络可达"?"目标 IP / 主机名":"Build 下限";parameterBLabel.Text="Build 上限（可空）";b.Visible=parameterBLabel.Visible=type.Text=="如果系统版本";c.Visible=parameterCLabel.Visible=false;a.AccessibleName=parameterALabel.Text;b.AccessibleName=parameterBLabel.Text;guide.Text=type.Text=="如果网络可达"?"填写一个 IP 或主机名，例如 192.168.1.1。Ping 有回应执行左栏，无回应执行右栏；禁止 Ping 时也可能无回应。":type.Text=="如果文件存在"?"选择完整文件路径。文件存在执行左栏，不存在执行右栏；不需要执行的分支可以留空。":"填写 Windows Build 范围，例如下限 22000、上限 26100。范围内执行左栏，范围外执行右栏。";}
                    else if(random){parameterALabel.Text="最短等待（秒）";parameterBLabel.Text="最长等待（秒）";a.AccessibleName=parameterALabel.Text;b.AccessibleName=parameterBLabel.Text;c.Visible=parameterCLabel.Visible=false;if(!randomInitialized){randomInitialized=true;a.Text="1";b.Text="20";c.Text="";}}
                    else if(!randomIp){a.AccessibleName="参数 1";b.AccessibleName="参数 2";}

                };
                type.SelectedIndexChanged+=delegate {refreshActionOptions();};
                guide.Text=AutomationService.Guide(type.Text);
                refreshActionOptions();
                return f.ShowDialog(this)==DialogResult.OK?result:null;
            }
        }
    }
}
