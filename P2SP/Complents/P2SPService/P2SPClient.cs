﻿/*
 * Created by SharpDevelop.
 * User: Administrator
 * Date: 2016/10/13 星期四
 * Time: 上午 10:34
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Threading;

using Helper;

namespace Complents.P2SPService
{
	/// <summary>
	/// Description of P2SPClient.
	/// </summary>
	public class P2SPClient
	{
		#region member data
		private bool isStart = false;
		private TcpListener listener;
		private Hashtable waitMap = new Hashtable();//[TransId.ToString(), BaseTrans]
		private static object waitLocker = new object();
		private static object sendLocker = new object();
		private static object recveLocker = new object();
		private Queue recveQueue = new Queue();
		private Queue sendQueue = new Queue();
		
		#endregion
		
		#region Event
		private event P2SPStartHandler P2SPStart;
		private event P2SPStopHandler P2SPStop;
		private event P2SPAcceptHandler P2SPAccept;
		public event P2SPListenerStartHandler ListenerStart;
		public event P2SPListenerStopHandler ListenerStop;
		public event P2SPConnectHandler P2SPConnect;
		public event P2SPDisConnectHandler P2SPDisConnect;
		public event P2SPRecveHandler P2SPRecve;
		public event P2SPSendEventHandler P2SPSend;
		
		#endregion
		
		#region contructer
		
		public P2SPClient()
		{
			this.P2SPAccept += new P2SPAcceptHandler(P2SPClient_P2SPAccept);
			this.P2SPStart += new P2SPStartHandler(P2SPClient_P2SPStart);
			this.P2SPStop += new P2SPStopHandler(P2SPClient_P2SPStop);
		}
		
		#endregion
		
		#region base class virtual function
		
		private void OnP2SPAccept(P2SPAcceptEventArgs e)
		{
			if (this.P2SPAccept != null)
			{
				this.P2SPAccept(this, e);
			}
		}
		
		private void OnP2SPStart(P2SPStartEventArgs e)
		{
			if (this.P2SPStart != null)
			{
				this.P2SPStart(this, e);
			}
		}
		
		protected virtual void OnP2SPStop(P2SPStopEventArgs e)
		{
			if (this.P2SPStop != null)
			{
				this.P2SPStop(this, e);
			}
		}
		
		protected virtual void OnP2SPListenerStart(P2SPListenerStartEventArgs e)
		{
			if (this.ListenerStart != null)
			{
				this.ListenerStart(this, e);
			}
		}
		
		protected virtual void OnP2SPListenerStop(P2SPListenerStopEventArgs e)
		{
			if (this.ListenerStop != null)
			{
				this.ListenerStop(this, e);
			}
		}
		
		protected virtual void OnP2SPConnect(P2SPConnectEventArgs e)
		{
			if (this.P2SPConnect != null)
			{
				this.P2SPConnect(this, e);
			}
		}
		
		protected virtual void OnP2SPDisConnect(P2SPDisConnectEventArgs e)
		{
			if (this.P2SPDisConnect != null)
			{
				this.P2SPDisConnect(this, e);
			}
		}
		
		protected virtual void OnP2SPRecve(P2SPRecveEventArgs e)
		{
			if (this.P2SPRecve != null)
			{
				this.P2SPRecve(this, e);
			}
		}
		
		protected virtual void OnP2SPSend(P2SPSendEventArgs e)
		{
			if (this.P2SPSend != null)
			{
				this.P2SPSend(this, e);
			}
		}
		
		#endregion
		
		#region fire event function
		
		void P2SPClient_P2SPStop(object sender, P2SPStopEventArgs args)
		{
			
		}

		void P2SPClient_P2SPStart(object sender, P2SPStartEventArgs args)
		{
			try
			{
				IPAddress[] ip = Dns.GetHostAddresses(Dns.GetHostName());
				this.listener = new TcpListener(ip[0] ,args.Port);
				this.listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				this.listener.Start();
				this.OnP2SPListenerStart(new P2SPListenerStartEventArgs());
				while(this.isStart)
				{
					TcpClient client = this.listener.AcceptTcpClient();
					this.OnP2SPAccept(new P2SPAcceptEventArgs(client));
				}
				this.OnP2SPListenerStop(new P2SPListenerStopEventArgs());
			}
			catch(Exception ex)
			{
				AutoLog.Instance.Error(null, ex);
			}
		}

		void P2SPClient_P2SPAccept(object sender, P2SPAcceptEventArgs args)
		{
			this.Recve(args.Client);
		}
		
		#endregion
		
		#region public interfaces
		
		public void Start(int _port)
		{
			AsyncHelper.AsyncHandler ah = new AsyncHelper.AsyncHandler(SendTrans);
			ah.BeginInvoke(null, null);
			
			ah = new AsyncHelper.AsyncHandler(RecveTrans);
			ah.BeginInvoke(null, null);
			
			this.OnP2SPStart(new P2SPStartEventArgs(_port));
		}
		
		public void Stop()
		{
			this.isStart = false;
			this.listener.Stop();
			this.OnP2SPStop(new P2SPStopEventArgs());
		}
		
		public void Connect(IPEndPoint _remoteEP)
		{
			TcpClient client = new TcpClient(AddressFamily.InterNetwork);
			client.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress, true);
			client.BeginConnect(_remoteEP.Address, _remoteEP.Port, new AsyncCallback(ConnectCb), client);
		}
		
		public BaseTrans Send(BaseTrans _trans)
		{
			lock(waitLocker)
			{
				this.waitMap.Add(_trans.TransId.ToString(), _trans);
			}
			lock(sendLocker)
			{
				sendQueue.Enqueue(_trans);
				Monitor.Pulse(sendLocker);
			}
			//may wait use the semphone wait
			if (_trans.IsNeedResp)
			{
				return _trans.WaitResp();
			}
			else
			{
				_trans.WaitAck();
				return null;
			}
		}
		
		#endregion
		
		#region private interfaces
		
		private void ConnectCb(IAsyncResult ia)
		{
			TcpClient client = ia.AsyncState as TcpClient;
			client.EndConnect(ia);
			this.OnP2SPConnect(new P2SPConnectEventArgs(client));
		}
		
		private void RecveTrans()
		{
			while(this.isStart)
			{
				lock(recveLocker)
				{
					if (this.recveQueue.Count == 0)
					{
						bool rslt = Monitor.Wait(recveLocker, 30*1000);
						if (!rslt)
						{
							//timeout
						}
						continue;
					}
					else
					{
						BaseTrans trans = recveQueue.Dequeue() as BaseTrans;
						if (trans.Type == TransType.eAck)
						{
							this.RecveAck(trans);
						}
						else if (trans.Type == TransType.eResp)
						{
							this.SendAck(trans);
							this.RecveResp(trans);
                            //this.OnP2SPRecve(new P2SPRecveEventArgs(trans));
                            trans.OnTransProcess(new EventArgs());
						}
						else
						{
							this.SendAck(trans);
                            //this.OnP2SPRecve(new P2SPRecveEventArgs(trans));
                            trans.OnTransProcess(new EventArgs());
                        }
					}
				}
			}
		}
		
		private void SendTrans()
		{
			while(this.isStart)
			{
				lock(sendLocker)
				{
					if (this.sendQueue.Count == 0)
					{
						bool rslt = Monitor.Wait(sendLocker, 30*1000);
						if (!rslt)
						{
							//timeout
						}
						continue;
					}
					else
					{
						BaseTrans trans = sendQueue.Dequeue() as BaseTrans;
						TcpClient client = trans.Client;
						try
						{
							ReadWriteObject readWriteObject = new ReadWriteObject();
							SendCell cell = new SendCell(trans);
							readWriteObject.SetSendBuff(cell.ToBuffer());
							NetworkStream ns = client.GetStream();
							if (ns.CanWrite)
							{
								ns.Write(readWriteObject.SendBuff, 0, readWriteObject.SendBuff.Length);
								this.OnP2SPSend(new P2SPSendEventArgs());
							}
							else
							{
								AutoLog.Instance.Fatal("can not to write");
							}
						}
						catch(Exception ex)
						{
							AutoLog.Instance.Error("disconnect", ex);
							this.OnP2SPDisConnect(new P2SPDisConnectEventArgs(client));
						}
					}
				}
			}
		}
		
		private void SendAck(BaseTrans _trans)
		{
			BaseTrans ackTrans = new AckTrans(_trans.TransId);
			lock(sendLocker)
			{
				sendQueue.Enqueue(_trans);
				Monitor.Pulse(sendLocker);
			}
		}
		
		private void RecveResp(BaseTrans _trans)
		{
			RespTrans respTrans = _trans as RespTrans;
			BaseTrans trans = null;
			lock(waitLocker)
			{
				trans = this.waitMap[respTrans.ReqId.ToString()] as BaseTrans;
				this.waitMap.Remove(trans.TransId.ToString());
			}
			trans.RecveResp(respTrans);
		}
		
		private void RecveAck(BaseTrans _trans)
		{
			AckTrans ackTrans = _trans as AckTrans;
			BaseTrans trans = null;
			lock(waitLocker)
			{
				trans = this.waitMap[ackTrans.ReqId.ToString()] as BaseTrans;
				this.waitMap.Remove(trans.TransId.ToString());
			}
			trans.RecvAck();
		}
		
		private void Recve(TcpClient _client)
		{
			try
			{
				NetworkStream ns = _client.GetStream();
				if (ns != null && ns.CanRead)
				{
					ReadWriteObject readWriteObject = new ReadWriteObject();
					readWriteObject.SetReadBuff(sizeof(int), true);
					while(true)
					{
						if (ns.CanRead)
						{
							int bytes = ns.Read(
								readWriteObject.ReadBuff,
								readWriteObject.TotalReadLength,
								readWriteObject.ReadBuff.Length - readWriteObject.TotalReadLength);
							if (bytes == 0)
							{
								this.OnP2SPDisConnect(new P2SPDisConnectEventArgs(_client));
							}
							readWriteObject.TotalReadLength += bytes;
							if (readWriteObject.IsHeader)
							{
								if (readWriteObject.TotalReadLength == readWriteObject.ReadBuff.Length)
								{
									int len = BitConverter.ToInt32(readWriteObject.ReadBuff, 0);
									readWriteObject.SetReadBuff(len, false);
								}
							}
							else
							{
								if (readWriteObject.TotalReadLength == readWriteObject.ReadBuff.Length)
								{
									SendCell cell = new SendCell();
									cell.FromBuffer(readWriteObject.ReadBuff);
									BaseTrans trans = (BaseTrans)(cell.Data);
									trans.Client = _client;
									lock(recveLocker)
									{
										recveQueue.Enqueue(trans);
										Monitor.Pulse(recveLocker);
									}
									readWriteObject.SetReadBuff(sizeof(int), true);
								}
							}
						}
						else
						{
							AutoLog.Instance.Fatal("can not to read");
						}
					}
				}
			}
			catch(Exception ex)
			{
				AutoLog.Instance.Error("disconnect", ex);
				this.OnP2SPDisConnect(new P2SPDisConnectEventArgs(_client));
			}
		}

		#endregion
	}
}