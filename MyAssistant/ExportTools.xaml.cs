﻿using Microsoft.Win32;
using MyAssistant.Common;
using MyAssistant.ViewModel;
using NLog.Fluent;
using NPOI.HSSF.Record.PivotTable;
using NPOI.SS.Formula.Eval;
using NPOI.SS.UserModel;
using OpenPop.Mime;
using OpenPop.Pop3;
using OpenPop.Pop3.Exceptions;
using ServiceStack;
using ServiceStack.Configuration;
using ServiceStack.Host;
using ServiceStack.Redis;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

namespace MyAssistant
{
    /// <summary>
    /// UpdateManage.xaml 的交互逻辑
    /// </summary>
    public partial class ExportTools : Window
    {
        public static IAppSettings appSettings = new AppSettings();
        public static string yanchangRoot;
        public static string ansenRoot;
        public static string yanchangs;
        public static string ansens;

        public ExportTools()
        {
            InitializeComponent();

            #region  创建消息通知
            ShowMessage("测试工具已启动");
            #endregion
        }

        private void ShowMessage(string message, int? delay = 5)
        {
            TaskBar bar = new TaskBar();
            bar.Left = SystemParameters.WorkArea.Size.Width - bar.Width;
            bar.Top = SystemParameters.WorkArea.Size.Height - bar.Height;

            bar.Topmost = true;

            bar.txtMessage.Text = message;
            bar.Show();
            if (delay.HasValue)
                bar.CloseMessage(delay.Value);
        }


        protected object GetCellValue(ICell item)
        {
            if (item == null)
            {
                return string.Empty;
            }
            switch (item.CellType)
            {
                case CellType.Boolean:
                    return item.BooleanCellValue;

                case CellType.Error:
                    return ErrorEval.GetText(item.ErrorCellValue);
                case CellType.Formula:
                    switch (item.CachedFormulaResultType)
                    {
                        case CellType.Boolean:
                            return item.BooleanCellValue;

                        case CellType.Error:
                            return ErrorEval.GetText(item.ErrorCellValue);

                        case CellType.Numeric:
                            if (DateUtil.IsCellDateFormatted(item))
                            {
                                return item.DateCellValue.ToString("yyyy-MM-dd HH:mm:ss");
                            }
                            else
                            {
                                return item.NumericCellValue;
                            }
                        case CellType.String:
                            var str = item.StringCellValue;
                            if (!string.IsNullOrEmpty(str))
                            {
                                return str;
                            }
                            else
                            {
                                return string.Empty;
                            }
                        case CellType.Unknown:
                        case CellType.Blank:
                        default:
                            return string.Empty;
                    }
                case CellType.Numeric:
                    if (DateUtil.IsCellDateFormatted(item))
                    {
                        return item.DateCellValue.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                    else
                    {

                        return item.NumericCellValue;
                    }
                case CellType.String:
                    var strValue = item.StringCellValue;
                    return strValue.Trim();

                case CellType.Unknown:
                case CellType.Blank:
                default:
                    return string.Empty;
            }
        }

        private void btnTemplateExport_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnNPOIExport_Click(object sender, RoutedEventArgs e)
        {
            var executablePathRoot = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            var filePath = System.IO.Path.Combine(executablePathRoot, "DHTable.xlsx");
            IWorkbook workbook = null;
            ISheet sheet = null;
            try
            {
                workbook = WorkbookFactory.Create(filePath);
                sheet = workbook.GetSheetAt(2);//获取第2个工作薄

                var tableName = "";
                var fieldName = "";
                var fieldType = "";

                Dictionary<string, string> fields = new Dictionary<string, string>();

                for (var j = 1; j < 62; j++)
                {
                    var row = sheet.GetRow(j);

                    if (row.Cells.Count == 4)
                    {
                        var v = GetCellValue(row.Cells[0]);
                        tableName = v.ToString();
                        var key = GetCellValue(row.Cells[1]);
                        fieldName = key.ToString();
                        var value = GetCellValue(row.Cells[2]);
                        fieldType = value.ToString();

                        fieldType = ConvertToDBType(fieldType);

                        fieldName = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(fieldName);

                        fields.Add(fieldName, fieldType);
                    }
                    else
                    {
                        int i = 0;
                        foreach (var cell in row.Cells)
                        {
                            var v = GetCellValue(cell);
                            if (i == 0)
                            {
                                fieldName = v.ToString();
                            }
                            if (i == 1)
                            {
                                fieldType = v.ToString();
                            }
                            i++;
                        }

                        fieldType = ConvertToDBType(fieldType);

                        fieldName = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(fieldName);
                        fields.Add(fieldName, fieldType);
                    }
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"CREATE TABLE {tableName} (");
                foreach (var item in fields)
                {
                    if (item.Key == "Id")
                    {
                        sb.AppendLine($"{item.Key} {item.Value}  PRIMARY KEY  AUTO_INCREMENT,");
                    }
                    else
                    {
                        sb.AppendLine($"{item.Key} {item.Value},");
                    }
                }
                sb.AppendLine($")");


                txtResult.Document.Blocks.Clear();

                Paragraph p = new Paragraph();
                Run r = new Run(sb.ToString());
                p.Inlines.Add(r);
                txtResult.Document.Blocks.Add(p);
            }
            catch (Exception ex)
            {
                Console.WriteLine("获取excel数据出错" + ex.Message);
                workbook?.Close();

            }
        }

        private string ConvertToDBType(string type)
        {
            var transeType = type;
            switch (type.ToLower())
            {
                case "string":
                    transeType = "varchar(100)";
                    break;

                case "double":
                    transeType = "double(13,3)";
                    break;

                case "datetime":
                    transeType = "date";
                    break;

                case "long":
                    transeType = "bigint";
                    break;
            }

            return transeType;
        }

        private void btnNPOIExportEntity_Click(object sender, RoutedEventArgs e)
        {
            var executablePathRoot = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;
            var filePath = System.IO.Path.Combine(executablePathRoot, "DHTable.xlsx");
            IWorkbook workbook = null;
            ISheet sheet = null;
            try
            {
                workbook = WorkbookFactory.Create(filePath);
                sheet = workbook.GetSheetAt(2);//获取第二个工作薄

                var tableName = "";
                var fieldName = "";
                var fieldType = "";
                //描述
                var fieldDes = "";

                Dictionary<string, string> fields = new Dictionary<string, string>();

                for (var j = 1; j < 62; j++)
                {
                    var row = sheet.GetRow(j);

                    if (row.Cells.Count == 4)
                    {
                        var v = GetCellValue(row.Cells[0]);
                        tableName = v.ToString();
                        var key = GetCellValue(row.Cells[1]);
                        fieldName = key.ToString();
                        var value = GetCellValue(row.Cells[2]);
                        fieldType = value.ToString();

                        value = GetCellValue(row.Cells[3]);
                        fieldDes = value.ToString();

                        fieldName = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(fieldName);

                        fields.Add(fieldName, fieldType + "," + fieldDes);
                    }
                    else
                    {
                        int i = 0;
                        foreach (var cell in row.Cells)
                        {
                            var v = GetCellValue(cell);
                            if (i == 0)
                            {
                                fieldName = v.ToString();
                            }
                            if (i == 1)
                            {
                                fieldType = v.ToString();
                            }
                            if (i == 2)
                            {
                                fieldDes = v.ToString();
                            }
                            i++;
                        }
                        fieldName = fieldName.Substring(0, 1).ToUpper() + fieldName.Substring(1);

                        fields.Add(fieldName, fieldType + "," + fieldDes);
                    }
                }

                StringBuilder sb = new StringBuilder();

                sb.AppendLine($"[Alias(\"{tableName}\")]");
                sb.AppendLine($"public class {tableName}");
                sb.AppendLine("{");

                foreach (var item in fields)
                {
                    var v = item.Value;
                    var vd = v.Split(',');

                    var aliasName = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(item.Key);

                    if (item.Key == "Id")
                    {
                        sb.AppendLine("[Index]");
                        sb.AppendLine("[AutoIncrement]");
                        sb.AppendLine(" /// <summary>");
                        sb.AppendLine($" /// {vd[1]}");
                        sb.AppendLine(" /// </summary>");

                        sb.AppendLine("[Alias(\"" + aliasName + "\")]");

                        sb.AppendLine("public   " + vd[0] + "  " + item.Key + " { set; get; }");
                    }
                    else
                    {
                        sb.AppendLine(" /// <summary>");
                        sb.AppendLine($" /// {vd[1]}");
                        sb.AppendLine(" /// </summary>");
                        sb.AppendLine("[Alias(\"" + aliasName + "\")]");
                        sb.AppendLine("public   " + vd[0] + "  " + item.Key + " { set; get; }");
                    }
                }
                sb.AppendLine("}");

                txtResult.Document.Blocks.Clear();

                Paragraph p = new Paragraph();
                Run r = new Run(sb.ToString());
                p.Inlines.Add(r);
                txtResult.Document.Blocks.Add(p);
            }
            catch (Exception ex)
            {
                Console.WriteLine("获取excel数据出错" + ex.Message);
                workbook?.Close();

            }
        }
    }
}