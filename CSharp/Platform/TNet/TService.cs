﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.Base;
using Common.Network;
using MongoDB.Bson;

namespace TNet
{
	public class TService: IService
	{
		private readonly IPoller poller = new TPoller();
		private TSocket acceptor;

		private readonly Dictionary<string, TChannel> channels = new Dictionary<string, TChannel>();

		private readonly Dictionary<ObjectId, TChannel> idChannels = new Dictionary<ObjectId, TChannel>();

		private readonly TimerManager timerManager = new TimerManager();

		private bool isStop;

		/// <summary>
		/// 即可做client也可做server
		/// </summary>
		/// <param name="host"></param>
		/// <param name="port"></param>
		public TService(string host, int port)
		{
			this.acceptor = new TSocket(this.poller);
			this.acceptor.Bind(host, port);
			this.acceptor.Listen(100);
		}

		/// <summary>
		/// 只能做client端的构造函数
		/// </summary>
		public TService()
		{
		}

		protected virtual void Dispose(bool disposing)
		{
			if (this.acceptor == null)
			{
				return;
			}

			if (disposing)
			{
				foreach (ObjectId id in idChannels.Keys.ToArray())
				{
					TChannel channel = idChannels[id];
					channel.Dispose();
				}
				this.acceptor.Dispose();
			}

			isStop = true;
			this.acceptor = null;
		}

		~TService()
		{
			this.Dispose(false);
		}

		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}

		public void Add(Action action)
		{
			this.poller.Add(action);
		}

		public AChannel GetChannel(ObjectId id)
		{
			TChannel channel = null;
			this.idChannels.TryGetValue(id, out channel);
			return channel;
		}

		private async Task<AChannel> ConnectAsync(string host, int port)
		{
			TSocket newSocket = new TSocket(this.poller);
			await newSocket.ConnectAsync(host, port);
			TChannel channel = new TChannel(newSocket, this);
			this.channels[newSocket.RemoteAddress] = channel;
			this.idChannels[channel.Id] = channel;
			return channel;
		}

		public async Task<AChannel> GetChannel()
		{
			if (this.acceptor == null)
			{
				throw new Exception(string.Format("service construct must use host and port param"));
			}
			TSocket socket = new TSocket(this.poller);
			await this.acceptor.AcceptAsync(socket);
			TChannel channel = new TChannel(socket, this);
			this.channels[channel.RemoteAddress] = channel;
			this.idChannels[channel.Id] = channel;
			return channel;
		}

		public void Remove(AChannel channel)
		{
			TChannel tChannel = channel as TChannel;
			if (tChannel == null)
			{
				return;
			}
			this.idChannels.Remove(channel.Id);
			this.channels.Remove(channel.RemoteAddress);
			this.timerManager.Remove(tChannel.SendTimer);
		}

		public async Task<AChannel> GetChannel(string host, int port)
		{
			TChannel channel = null;
			if (this.channels.TryGetValue(host + ":" + port, out channel))
			{
				return channel;
			}
			return await this.ConnectAsync(host, port);
		}

		public void RunOnce(int timeout)
		{
			this.poller.Run(timeout);
		}

		public void Start()
		{
			while (!isStop)
			{
				this.RunOnce(0);
				this.timerManager.Refresh();
			}
		}

		public void Stop()
		{
			this.isStop = true;
		}

		internal TimerManager Timer
		{
			get
			{
				return this.timerManager;
			}
		}
	}
}