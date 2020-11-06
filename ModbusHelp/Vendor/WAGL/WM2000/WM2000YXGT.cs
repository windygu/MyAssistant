﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using ServiceStack;
using ServiceStack.Redis;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication.ModBus;
using YCIOT.ModbusPoll.RtuOverTcp.Utils;
using YCIOT.ModbusPoll.RtuOverTcp;
using YCIOT.ServiceModel;
using Acme.Common.Utils;
using ServiceStack.Configuration;
using YCIOT.ServiceModel.OilWell;

namespace YCIOT.ModbusPoll.Vendor.WAGL.WM2000
{
    public static class WM2000YXGT
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly bool isDebug = new AppSettings().Get<bool>("Modbus.IsDebug");

        public static async Task Get_XAGL_WM2000YXGT_IndicatorDiagram(ModbusRtuOverTcp client, RedisClient redisClient, string messageString)
        {
            var par = messageString.FromJson<ControlRequest>();
            try
            {
                var indicatorDiagram = new IotDataOilWellIndicatorDiagram()
                {
                    AlarmCode = 0,
                    AlarmMsg = "正常"
                };

                var logIotModbusPoll = par.ConvertTo<LogIotModbusPoll>();

                logIotModbusPoll.State = 0;
                logIotModbusPoll.Result = "ok";

                var modbusAddress = par.ModbusAddress;
                indicatorDiagram.DeviceTypeId = par.DeviceTypeId;
                indicatorDiagram.Mock = false;

                var flag = true;

                var jo1 = (JObject)JsonConvert.DeserializeObject(par.CommandParameter);
                if (jo1["0"] != null)
                    indicatorDiagram.Displacement = Convert.ToDouble(jo1["0"].ToString());

                ClientInfo.CurrentModbusPoolAddress = modbusAddress;

                indicatorDiagram.D = new List<double>();  //位移
                indicatorDiagram.L = new List<double>();  //载荷

                indicatorDiagram.DateTime = DateTime.Now;

                indicatorDiagram.WellId = par.DeviceId;


                #region  判断井状态

                lock (ClientInfo.locker)
                {
                    ClientInfo.RequestTime = DateTime.Now;
                    ClientInfo.ExpectedType = 0x03;
                    ClientInfo.ExpectedDataLen = 2;
                }

                var read = await client.ReadAsync($"s={par.ModbusAddress};x=3;4103", 1);
                if (read.IsSuccess)
                {
                    var value = client.ByteTransform.TransInt16(read.Content, 0);

                    if (value == 3)  //1：正转运行；3：变频停机
                    {
                        indicatorDiagram.NetworkNode = ClientInfo.ManyIpAddress;
                        indicatorDiagram.AlarmCode = 3;
                        indicatorDiagram.AlarmMsg = "停井";
                    }
                }
                else
                {
                    flag = false;
                    indicatorDiagram.AlarmCode = -1;
                    indicatorDiagram.AlarmMsg = "数据异常";

                    indicatorDiagram.Mock = par.UseMockData;
                    logIotModbusPoll.DateTime = DateTime.Now;
                    logIotModbusPoll.State = -1;
                    logIotModbusPoll.Result = $"读取功图井状态数据异常！[{read.Message}]";
                    $"GT:{par.DeviceName}-{par.DeviceId}{logIotModbusPoll.Result}".Info();

                    redisClient.AddItemToList("YCIOT:ERROR:Log_IOT_Modbus_Poll", logIotModbusPoll.ToJson().IndentJson());
                }
                #endregion


                const ushort step = 100;

                //ToDo:确认采样间隔和点数

                indicatorDiagram.Count = 300;

                if (flag && indicatorDiagram.Count <= 300 && indicatorDiagram.Count >= step)
                {
                    ushort regAddress = 38768;//读取载荷数据

                    for (ushort i = 0; i < indicatorDiagram.Count && flag; i += step)
                    {
                        var itemCount = (i + step > indicatorDiagram.Count) ? (ushort)(indicatorDiagram.Count - i) : step;
                        if (isDebug)
                            Logger.Info($"{i}:{itemCount}:{(ushort)(regAddress + i)}");

                        lock (ClientInfo.locker)
                        {
                            ClientInfo.RequestTime = DateTime.Now;
                            ClientInfo.ExpectedType = 0x03;
                            ClientInfo.ExpectedDataLen = itemCount * 2;
                        }

                        read = await client.ReadAsync($"s={par.ModbusAddress};x=3;{(regAddress + i)}", itemCount);

                        if (!read.IsSuccess)
                        {
                            read = await client.ReadAsync($"s={par.ModbusAddress};x=3;{(regAddress + i)}", itemCount);
                        }

                        if (read.IsSuccess)
                        {
                            for (var j = 0; j < itemCount; j++)
                            {
                                var value = client.ByteTransform.TransInt16(read.Content, j * 2);
                                if (value != 0)
                                {
                                    var Load = (value - 800) * 150 / 3200.0;
                                    indicatorDiagram.L.Add(Math.Round(Load, 2));
                                }
                                else
                                {
                                    indicatorDiagram.L.Add(value);
                                }
                            }
                        }
                        else
                        {
                            flag = false;

                            indicatorDiagram.AlarmCode = -1;
                            indicatorDiagram.AlarmMsg = "数据异常";

                            indicatorDiagram.Mock = par.UseMockData;
                            logIotModbusPoll.Type = "Get_XAGL_WM2000YXGT_IndicatorDiagram";
                            logIotModbusPoll.DateTime = DateTime.Now;
                            logIotModbusPoll.State = -1;
                            logIotModbusPoll.Result = "从 " + (regAddress + i).ToString() + " 个开始，读取 " + itemCount.ToString() + $" 个有线功图载荷数据异常![{read.Message}]";

                            redisClient.AddItemToList("YCIOT:ERROR:Log_IOT_Modbus_Poll", logIotModbusPoll.ToJson().IndentJson());
                        }
                        Thread.Sleep(100);
                    }

                    regAddress = 37268;//读取位移数据
                    for (var i = 0; i < indicatorDiagram.Count && flag; i += step)
                    {
                        var itemCount = (i + step > indicatorDiagram.Count) ? (ushort)(indicatorDiagram.Count - i) : step;
                        if (isDebug)
                            Logger.Info($"{i}:{itemCount}:{(ushort)(regAddress + i)}");

                        lock (ClientInfo.locker)
                        {
                            ClientInfo.RequestTime = DateTime.Now;
                            ClientInfo.ExpectedType = 0x03;
                            ClientInfo.ExpectedDataLen = itemCount * 2;
                        }
                        read = await client.ReadAsync($"s={par.ModbusAddress};x=3;{(regAddress + i)}", itemCount);

                        if (!read.IsSuccess)
                        {
                            read = await client.ReadAsync($"s={par.ModbusAddress};x=3;{(regAddress + i)}", itemCount);
                        }

                        if (read.IsSuccess)
                        {
                            for (var j = 0; j < itemCount; j++)
                            {
                                var value = client.ByteTransform.TransInt16(read.Content, j * 2);

                                var d = (value - 800) * 100 / 3200.0 - 50;
                                indicatorDiagram.D.Add(Math.Round(d, 2));
                            }
                        }
                        else
                        {
                            flag = false;

                            indicatorDiagram.AlarmCode = -1;
                            indicatorDiagram.AlarmMsg = "数据异常";

                            indicatorDiagram.Mock = par.UseMockData;
                            logIotModbusPoll.Type = "Get_XAGL_WM2000YXGT_IndicatorDiagram";
                            logIotModbusPoll.DateTime = DateTime.Now;
                            logIotModbusPoll.State = -1;
                            logIotModbusPoll.Result = "从 " + (regAddress + i).ToString() + " 个开始，读取 " +
                                                      itemCount.ToString() + $" 个有线功图位移数据异常![{read.Message}]";

                            redisClient.AddItemToList("YCIOT:ERROR:Log_IOT_Modbus_Poll",
                                logIotModbusPoll.ToJson().IndentJson());
                        }

                        Thread.Sleep(100);
                    }

                    var maxDis = indicatorDiagram.D.Max();
                    var minDis = indicatorDiagram.D.Min();

                    if (!indicatorDiagram.Displacement.HasValue)
                    {
                        indicatorDiagram.Displacement = maxDis;
                    }

                    for (var i = 0; i < indicatorDiagram.D.Count; i++)
                    {
                        if (Math.Abs(maxDis - minDis) > 0.1)
                        {
                            indicatorDiagram.D[i] = Math.Round(((indicatorDiagram.D[i] - minDis) / (maxDis - minDis) * (double)indicatorDiagram.Displacement), 2);
                        }
                    }

                    if (indicatorDiagram.D.Count > 0)
                    {
                        indicatorDiagram.MaxLoad = Math.Round(indicatorDiagram.L.Max(), 2);//最大载荷
                        indicatorDiagram.MinLoad = Math.Round(indicatorDiagram.L.Min(), 2);//最小载荷
                        indicatorDiagram.AvgLoad = Math.Round(indicatorDiagram.L.Average(), 2);//平均载荷
                        indicatorDiagram.D.Add(indicatorDiagram.D[0]);
                        indicatorDiagram.L.Add(indicatorDiagram.L[0]);
                    }
                }
                else
                {
                    flag = false;
                }

                indicatorDiagram.NetworkNode = ClientInfo.ManyIpAddress;

                if (flag == true)
                {
                    $"YXGT:{par.DeviceName}-{par.DeviceId}已获取到数据".Info();
                    indicatorDiagram.Mock = par.UseMockData;

                    redisClient.AddItemToList("YCIOT:IOT_Data_OilWell_IndicatorDiagram", indicatorDiagram.ToJson().IndentJson());
                    redisClient.Set($"Group:OilWell:{par.DeviceName}-{par.DeviceId}:IndicatorDiagram", indicatorDiagram);
                    redisClient.Set($"Single:OilWell:IndicatorDiagram:{par.DeviceName}-{par.DeviceId}", indicatorDiagram);
                }

                //用于通过ServerEvent给调用着返回消息
                if (!par.UserName.IsNullOrEmpty())
                {
                    ServerEventHelper.SendSseMessage(par.UserName, par.SessionId, flag ? 0 : -2, indicatorDiagram.ToJson().IndentJson());
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
                Logger.Error(ex.StackTrace);
                Logger.Error(ex.Source);

                if (!par.UserName.IsNullOrEmpty())
                {
                    ServerEventHelper.SendSseMessage(par.UserName, par.SessionId, -1, ex.Message);
                }
            }


        }

        public static void Get_XAGL_WM2000YXGT_IndicatorDiagram_Mock(ModbusRtuOverTcp client, RedisClient redisClient, string messageString)
        {
            var par = messageString.FromJson<ControlRequest>();

            try
            {
                var indicatorDiagram = new IotDataOilWellIndicatorDiagram()
                {
                    AlarmCode = 0,
                    AlarmMsg = "正常"
                };

                indicatorDiagram.WellId = par.DeviceId;
                indicatorDiagram.DeviceTypeId = par.DeviceTypeId;
                indicatorDiagram.DateTime = DateTime.Now;
                indicatorDiagram.Mock = par.UseMockData;

                redisClient.AddItemToList("YCIOT:IOT_Data_OilWell_IndicatorDiagram", indicatorDiagram.ToJson().IndentJson());
                redisClient.Set($"Group:OilWell:{par.DeviceName}-{par.DeviceId}:IndicatorDiagram", indicatorDiagram);
                redisClient.Set($"Single:OilWell:IndicatorDiagram:{par.DeviceName}-{par.DeviceId}", indicatorDiagram);
            }
            catch (Exception ex)
            {
                Logger.Error(ex.Message);
                Logger.Error(ex.StackTrace);
                Logger.Error(ex.Source);

                if (!par.UserName.IsNullOrEmpty())
                {
                    ServerEventHelper.SendSseMessage(par.UserName, par.SessionId, -1, ex.Message);
                }
            }
        }

    }
}
