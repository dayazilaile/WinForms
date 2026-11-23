using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinForms
{
    public static class FileAssociationService
    {
        [DllImport("Shlwapi.dll",CharSet=CharSet.Unicode,EntryPoint="AssocQueryStringW")]
        private static extern int AssocQueryString(int flags,int str,string assoc,string extra,StringBuilder text,ref int size);
        public static string FriendlyName(string assoc)
        {
            try
            {
                int length=2048; StringBuilder output=new StringBuilder(length);
                int result=AssocQueryString(0,4,assoc,null,output,ref length);
                return result==0 && output.Length>0 ? output.ToString():"未识别（请到默认应用设置核实）";
            }
            catch { return "读取失败 / 不受支持"; }
        }
    }
}
