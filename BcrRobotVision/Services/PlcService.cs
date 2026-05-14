using System;
using System.Net.Sockets;

namespace BcrRobotVision.Services
{
    public class PlcService : IDisposable
    {
        private readonly object _modbusLock = new object();
        private TcpClient? _client;
        private ushort _transactionId = 0;

        public bool IsConnected => _client?.Connected == true;

        /// <summary>
        /// 批量读取 01X Coil Bool
        /// 例如读取 01x80 和 01x81：ReadCoils(80, 2)
        /// </summary>
        public bool[] ReadCoils(ushort startAddress, ushort count)
        {
            if (_client == null || !_client.Connected)
                throw new InvalidOperationException("PLC未连接");

            lock (_modbusLock)
            {
                NetworkStream stream = _client.GetStream();

                _transactionId++;

                byte unitId = 1;
                byte functionCode = 0x01; // 01功能码：读线圈

                byte[] frame = new byte[12];

                frame[0] = (byte)(_transactionId >> 8);
                frame[1] = (byte)(_transactionId & 0xFF);

                frame[2] = 0x00;
                frame[3] = 0x00;

                frame[4] = 0x00;
                frame[5] = 0x06;

                frame[6] = unitId;
                frame[7] = functionCode;

                frame[8] = (byte)(startAddress >> 8);
                frame[9] = (byte)(startAddress & 0xFF);

                frame[10] = (byte)(count >> 8);
                frame[11] = (byte)(count & 0xFF);

                stream.Write(frame, 0, frame.Length);

                byte[] response = new byte[256];
                int len = stream.Read(response, 0, response.Length);

                if (len < 10)
                    throw new Exception("PLC读取01X响应长度异常");

                if (response[7] == (functionCode | 0x80))
                    throw new Exception($"PLC读取01X异常码：{response[8]}");

                byte byteCount = response[8];

                bool[] values = new bool[count];

                for (int i = 0; i < count; i++)
                {
                    int byteIndex = 9 + i / 8;
                    int bitIndex = i % 8;

                    if (byteIndex >= 9 + byteCount)
                        break;

                    values[i] = (response[byteIndex] & (1 << bitIndex)) != 0;
                }

                return values;
            }
        }

        public bool Connect(string ip, int port)
        {
            Disconnect();

            _client = new TcpClient();
            _client.ReceiveTimeout = 1000;
            _client.SendTimeout = 1000;
            _client.Connect(ip, port);

            return IsConnected;
        }

        public void Disconnect()
        {
            try
            {
                _client?.Close();
                _client?.Dispose();
            }
            catch
            {
            }

            _client = null;
        }

        /// <summary>
        /// 读取 01X Coil Bool
        /// 例如 01x80 就传 80
        /// 如果PLC实际地址偏移一位，可尝试传 79
        /// </summary>
        public bool ReadCoil(ushort address)
        {
            if (_client == null || !_client.Connected)
                throw new InvalidOperationException("PLC未连接");

            NetworkStream stream = _client.GetStream();

            _transactionId++;

            byte unitId = 1;
            byte functionCode = 0x01; // Read Coils

            byte[] frame = new byte[12];

            frame[0] = (byte)(_transactionId >> 8);
            frame[1] = (byte)(_transactionId & 0xFF);

            frame[2] = 0x00;
            frame[3] = 0x00;

            frame[4] = 0x00;
            frame[5] = 0x06;

            frame[6] = unitId;
            frame[7] = functionCode;

            frame[8] = (byte)(address >> 8);
            frame[9] = (byte)(address & 0xFF);

            frame[10] = 0x00;
            frame[11] = 0x01;

            stream.Write(frame, 0, frame.Length);

            byte[] response = new byte[256];
            int len = stream.Read(response, 0, response.Length);

            if (len < 10)
                throw new Exception("PLC读取01X响应异常");

            if (response[7] == (functionCode | 0x80))
                throw new Exception($"PLC读取01X返回异常码：{response[8]}");

            return (response[9] & 0x01) == 0x01;
        }

        /// <summary>
        /// 写单个保持寄存器，当前用于写 OK=1 / NG=2
        /// 拍照1结果地址：100
        /// 拍照2结果地址：101
        /// </summary>
        public void WriteSingleRegister(ushort address, short value)
        {
            if (_client == null || !_client.Connected)
                throw new InvalidOperationException("PLC未连接");

            lock (_modbusLock)
            {
                NetworkStream stream = _client.GetStream();

                _transactionId++;

                byte unitId = 1;
                byte functionCode = 0x06;

                byte[] frame = new byte[12];

                frame[0] = (byte)(_transactionId >> 8);
                frame[1] = (byte)(_transactionId & 0xFF);

                frame[2] = 0x00;
                frame[3] = 0x00;

                frame[4] = 0x00;
                frame[5] = 0x06;

                frame[6] = unitId;
                frame[7] = functionCode;

                frame[8] = (byte)(address >> 8);
                frame[9] = (byte)(address & 0xFF);

                frame[10] = (byte)(value >> 8);
                frame[11] = (byte)(value & 0xFF);

                stream.Write(frame, 0, frame.Length);

                byte[] response = new byte[12];
                int len = stream.Read(response, 0, response.Length);

                if (len < 12)
                    throw new Exception("PLC写入响应异常");
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}