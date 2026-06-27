using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Faolan.Core.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Faolan.Core.Network
{
	public interface INetworkClient
	{
		string IpAddress { get; }

		/// <summary>True while the underlying socket is live. Goes false once the client disconnects.</summary>
		bool Connected { get; }

		Account Account { get; set; }
		Character Character { get; }

		void Send(byte[] value);
	}

	public abstract class NetworkClient : INetworkClient
	{
		public delegate void NetworkClientDelegate(INetworkClient client);

		public delegate Task ReceivedPacketDelegate(INetworkClient client, Packet packet);

		protected readonly ILogger Logger;
        protected Socket Socket;
        protected readonly bool _isAgentServer;

		public ReceivedPacketDelegate ReceivedPacket { get; set; }
		public NetworkClientDelegate Disconnected { get; set; }

		protected NetworkClient(Socket socket, ILogger logger, bool isAgentServer)
		{
			Socket = socket;
			Logger = logger;
            _isAgentServer = isAgentServer;
        }

		// Must never throw: it is read from the receive/send error paths and the connect/disconnect
		// loggers, which run exactly when the socket is being torn down. A disconnecting client nulls
		// (and now disposes) Socket on one path while another path reads IpAddress -> dereferencing a
		// null/disposed Socket threw an unhandled NullReferenceException that aborted the whole process.
		// Guard the null/disposed socket and cache the last known address so logs stay useful after teardown.
		private string _ipAddress;

		public string IpAddress
		{
			get
			{
				try
				{
					var ip = (Socket?.RemoteEndPoint as IPEndPoint)?.Address.ToString();
					if (ip != null) _ipAddress = ip;
				}
				catch
				{
					// Socket disposed mid-read; fall back to the cached value.
				}

				return _ipAddress;
			}
		}
        //public ushort LocalPort => (ushort)(((IPEndPoint)Socket.LocalEndPoint)?.Port ?? 0);

		public bool Connected => Socket != null;

		public Account Account { get; set; }
		public Character Character => Account?.Character;

		public abstract void Send(byte[] value);
		public abstract void Start();
	}

	public class NetworkClient<TPacket> : NetworkClient
		where TPacket : Packet
	{
		private const int MaxMessageSize = 4 * 1024 * 1024;

		private readonly object _lock = new();
		private readonly byte[] _packetLengthBuffer = new byte[sizeof(int)];
		private readonly byte[] _tcpBuffer = new byte[0xFFFF];

		private byte[] _packetBuffer;
		private int _packetBytesRead;
        //private Inflater _inflater;

        public NetworkClient(Socket socket, ILogger logger)
			: base(socket, logger, typeof(TPacket).Name.Contains("AgentServerPacket"))
        {
			//
        }

		public override void Start()
		{
			BeginReceive( /*token*/);
		}

		public override void Send(byte[] value)
		{
			/*var stream = new ConanStream(value);
			_ = stream.ReadUInt32();
			_ = stream.ReadUInt32();
			_ = stream.ReadUInt32();
			_ = stream.ReadByte();
			_ = stream.ReadArrayPrependLengthByte();
			_ = stream.ReadByte();
			_ = stream.ReadArrayPrependLengthByte();
			var opcode = stream.ReadUInt16();            
			Console.WriteLine($"Send opcode: 0x{opcode:X4}");*/

			lock (_lock)
			{
				try
				{
					Socket?.Send(value);

					/*Socket?.BeginSend(value, 0, value.Length, 0, ar =>
					{
					    try
					    {
					        Socket.EndSend(ar);
					    }
					    catch // (Exception e)
					    {
					        Disconnected?.Invoke(this);
					        Socket = null;
					    }
					}, null);*/
				}
				catch // (Exception e)
				{
					Disconnected?.Invoke(this);
					Socket?.Dispose();
					Socket = null;
				}
			}
		}

		private void BeginReceive( /*token*/)
		{
			try
			{
				Socket?.BeginReceive(_tcpBuffer, 0, _tcpBuffer.Length, 0, ar =>
				{
					try
					{
						var bytesRead = Socket.EndReceive(ar);
						if (bytesRead == 0)
						{
							// Graceful remote close: fire the disconnect path and tear the socket down,
							// otherwise the receive loop just stops and the stale client lingers (and
							// Connected would still report true).
							Disconnected?.Invoke(this);
							Socket?.Dispose();
							Socket = null;
							return;
						}

						var buffer = _tcpBuffer.Take(bytesRead).ToArray();

						// zlib compression (0x80000005)
						if (buffer[0] == 0x80 && buffer[1] == 0x00 && buffer[2] == 0x00 && buffer[3] == 0x05)
						{
                            BeginReceive();
							return;

                            //_inflater = new Inflater();
                            //buffer = Decompress(buffer.Skip(9).ToArray());
                        }
                        //else if (_inflater != null)
                        //    buffer = Decompress(buffer);

                        DataReceived(buffer, bytesRead);
						BeginReceive();
					}
					catch // (Exception e)
					{
						Disconnected?.Invoke(this);
						Socket?.Dispose();
						Socket = null;
					}
				}, null);
			}
			catch // (Exception e)
			{
				Disconnected?.Invoke(this);
				Socket?.Dispose();
				Socket = null;
			}
		}

		// https://blog.stephencleary.com/2009/04/message-framing.html
		private void DataReceived(byte[] data, int length)
		{
            if (_isAgentServer)
            {
                var packet = (TPacket)Activator.CreateInstance(typeof(TPacket), data);
                if (packet?.IsValid == true)
                    ReceivedPacket?.Invoke(this, packet);
                else
                    throw new Exception("packet?.IsValid != true");

                return;
            }

			var i = 0;
			while (i != length)
			{
				var bytesAvailable = data.Length - i;
				if (_packetBuffer != null)
				{
					// We're reading into the data buffer
					var bytesRequested = _packetBuffer.Length - _packetBytesRead;

					var bytesTransferred = Math.Min(bytesRequested, bytesAvailable);
					Array.Copy(data, i, _packetBuffer, _packetBytesRead, bytesTransferred);
					i += bytesTransferred;

					ReadCompleted(bytesTransferred);
				}
				else
				{
					// We're reading into the length prefix buffer
					var bytesRequested = _packetLengthBuffer.Length - _packetBytesRead;

					var bytesTransferred = Math.Min(bytesRequested, bytesAvailable);
					Array.Copy(data, i, _packetLengthBuffer, _packetBytesRead, bytesTransferred);
					i += bytesTransferred;

					ReadCompleted(bytesTransferred);
				}
			}
		}

		private void ReadCompleted(int count)
		{
			_packetBytesRead += count;

			if (_packetBuffer == null)
			{
				// We're currently receiving the length buffer
				if (_packetBytesRead == sizeof(int))
				{
					var length = BitConverter.ToInt32(_packetLengthBuffer.Reverse().ToArray(), 0);

					// Sanity check for length < 0
					if (length < 0)
						throw new ProtocolViolationException("Message length is less than zero");

					// Reject oversized frames to prevent denial-of-service via huge allocation.
					// THROW (don't return) — like the length<0 guard above: returning from here leaves
					// _packetBytesRead at sizeof(int) and DataReceived's `while (i != length)` loop never
					// advances (bytesTransferred stays 0), spinning forever. The throw unwinds to the
					// BeginReceive callback's catch, which disconnects cleanly.
					if (length > MaxMessageSize)
						throw new ProtocolViolationException($"Message length {length} exceeds maximum {MaxMessageSize}");

					// Zero-length packets are allowed as keepalives
					if (length == 0)
					{
						_packetBytesRead = 0;
						//GotCompletePacket(Array.Empty<byte>());
					}
					else
					{
						// Create the data buffer and start reading into it
						_packetBuffer = new byte[length];
						_packetBytesRead = 0;
					}
				}
			}
			else
			{
				if (_packetBytesRead == _packetBuffer.Length)
				{
					// We've gotten an entire packet, add back the length
					var completePacket = _packetLengthBuffer.Concat(_packetBuffer).ToArray();

					var packet = (TPacket)Activator.CreateInstance(typeof(TPacket), completePacket);
					if (packet?.IsValid == true)
						ReceivedPacket?.Invoke(this, packet);
					else
						throw new Exception("packet?.IsValid != true");

					// Start reading the length buffer again
					_packetBuffer = null;
					_packetBytesRead = 0;
				}
			}
		}

        /*private byte[] Decompress(byte[] input)
        {
            try
            {
                _inflater.SetInput(input);

                var bos = new MemoryStream(input.Length);

                var buf = new byte[10240];
                while (!_inflater.IsFinished && !_inflater.IsNeedingInput)
                {
                    var count = _inflater.Inflate(buf);
                    bos.Write(buf, 0, count);
                }

                return bos.ToArray();
            }
            catch //(Exception e)
            {
                return null;
            }
        }*/
    }
}
