using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio;
using NAudio.Wave;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Security
{
    class Program
    {
        const double Magic = 2;
        const double Weight = 0.1;

        static ManualResetEvent _reset = new ManualResetEvent(false);
        static object _locker = new object();
        static byte[] _buffer;
        static int _nBufferBytes;
        static double _powerMean = Magic;
        static double _powerLast;

        static void Main(string[] args)
        {
            Thread x = new Thread(new ThreadStart(() =>
            {
                while (true)
                {
                    _reset.WaitOne();
                    _reset.Reset();
                }
            }));
            x.Start();

            using (WaveInEvent waveIn = new WaveInEvent())
            {
                waveIn.DeviceNumber = 0;
                waveIn.BufferMilliseconds = 4000;
                waveIn.NumberOfBuffers = 2;
                waveIn.DataAvailable += waveIn_DataAvailable;
                waveIn.WaveFormat = new WaveFormat(11025, 8, 1);
                waveIn.StartRecording();

#if false
                // Establish the local endpoint for the socket.
                // Dns.GetHostName returns the name of the 
                // host running the application.
                IPHostEntry ipHostInfo = Dns.Resolve(Dns.GetHostName());
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 11000);

                // Create a TCP/IP socket.
                Socket listener = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);

                // Bind the socket to the local endpoint and 
                // listen for incoming connections.
                listener.Bind(localEndPoint);
                listener.Listen(10);

                // Start listening for connections.
                while (true)
                {
                    Console.WriteLine("Waiting for a connection...");
                    // Program is suspended while waiting for an incoming connection.
                    Socket sender = listener.Accept();

                    Thread t = new Thread(new ThreadStart(() =>
                    {
                        Console.WriteLine("Connected.");
                        while (sender.Connected)
                        {
                            _reset.WaitOne();

                            lock (_locker)
                            {
                                try
                                {
                                    int i = 0;
                                    for (; i + 1023 < _nBufferBytes; i += 1024)
                                    {
                                        sender.Send(_buffer, i, 1024, SocketFlags.None);
                                    }
                                    sender.Send(_buffer, i, _nBufferBytes % 1024, SocketFlags.None);
                                }
                                catch (SocketException)
                                {

                                }
                            }
                        }
                        Console.WriteLine("Disconnected.");
                    }));
                    t.Start();
                }
#endif

                while (true)
                {
                    _reset.WaitOne();

                    lock (_locker)
                    {
                        if (_powerLast / _powerMean > Magic)
                            Console.WriteLine("LOUD NOISES!");
                        else
                            Console.WriteLine("{0}\t{1}", _powerMean, _powerLast);
                    }
                }
            }
        }

        static void waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            WaveInEvent waveIn = (WaveInEvent)sender;

            lock (_locker)
            {
                _buffer = e.Buffer;
                _nBufferBytes = e.BytesRecorded;

                var vals = _buffer.Take(_nBufferBytes).Select(b => (double)b);
                double mean = vals.Average();
                double power = vals.Average(b => (mean - b) * (mean - b));

                _powerLast = power;
                if (power < _powerMean)
                    _powerMean = (1 - Weight) * power + Weight * _powerMean;
                else
                    _powerMean = Weight * power + (1 - Weight) * _powerMean;
            }
            _reset.Set();
        }
    }
}
