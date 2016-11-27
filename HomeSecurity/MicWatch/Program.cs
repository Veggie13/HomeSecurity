using Accord.Extensions.Imaging;
using AForge.Vision.Motion;
using DotImaging;
using NAudio.Lame;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Timers;

namespace MicWatch
{
    class Program
    {
        private const string FileRoot = @"E:\Dropbox\Public\Security";
        private static readonly string SnapshotFilename = Path.Combine(FileRoot, "snapshot.jpg");
        private static readonly string LogFilename = Path.Combine(FileRoot, "log.txt");

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        static extern IntPtr memcpy(IntPtr dest, IntPtr src, UIntPtr count);

        private static bool _running = true;

        private static bool _listening = false;

        private static object _logLocker = new object();
        private static FileStream _logStream;

        private static object _cameraLocker = new object();
        private static CameraCapture _camera;

        private static object _imageLocker = new object();
        private static Bgr<byte>[,] _image;
        private static IMotionDetector _detector = new TwoFramesDifferenceDetector(true);

        private static object _queueLocker = new object();
        private static Queue<float[]> _queue = new Queue<float[]>(new[] {
            new float[] { 0f }, new float[] { 0f }, new float[] { 0f }, new float[] { 0f },
            new float[] { 0f }, new float[] { 0f }, new float[] { 0f }, new float[] { 0f },
            new float[] { 0f }, new float[] { 0f }, new float[] { 0f }, new float[] { 0f },
            new float[] { 0f }, new float[] { 0f }, new float[] { 0f }, new float[] { 0f } });
        private static int _nHistoricalNoises = 0;

        private static object _doLogLocker = new object();
        private static bool _doLog = false;

        static void Main(string[] args)
        {
            using (_logStream = new FileStream(LogFilename, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                logMessage("Press enter to begin in 60 seconds.");
                Console.ReadLine();

                //System.Threading.Thread.Sleep(60000);

                LoudNoise += DoLog;
                LoudNoise += Program_LoudNoise;
                Movement += DoLog;
                Movement += Program_Movement;

                initCamera();
                var imageReadTimer = new Timer()
                {
                    AutoReset = true,
                    Interval = 1000,
                    Enabled = true
                };
                imageReadTimer.Elapsed += imageReadTimer_Elapsed;

                var imageWriteTimer = new Timer()
                {
                    AutoReset = true,
                    Interval = 60000,
                    Enabled = true
                };
                imageWriteTimer.Elapsed += imageWriteTimer_Elapsed;

                var logTimer = new Timer()
                {
                    AutoReset = true,
                    Interval = 2000,
                    Enabled = true
                };
                logTimer.Elapsed += logTimer_Elapsed;

                System.Threading.Thread t = new System.Threading.Thread(DoListen);
                t.Start();

                logMessage("Press enter to terminate.");

                Console.ReadLine();
                imageReadTimer.Stop();
                imageWriteTimer.Stop();
                logTimer.Stop();
                _running = false;
                _listening = false;
                t.Join();
                cleanupCamera();

                logMessage("Done.");
            }
        }

        static event Action Movement = delegate { };
        static event Action LoudNoise = delegate { };
        static event Action ExceptionLogged = delegate { };

        private static void DoListen()
        {
            ExceptionLogged += () => { _listening = false; };

            while (_running)
            {
                System.Threading.Thread.Sleep(10000);

                using (var waveIn = new WaveInEvent()
                {
                    DeviceNumber = 0,
                    BufferMilliseconds = 1000,
                    NumberOfBuffers = 4,
                    WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)
                })
                {
                    waveIn.DataAvailable += WaveIn_DataAvailable;
                    waveIn.StartRecording();
                    logMessage("Listening.");
                    _listening = true;
                    while (_listening)
                    {
                        System.Threading.Thread.Sleep(1000);
                    }
                    logMessage("Done listening.");
                }
            }
        }

        private static void Program_Movement()
        {
            logMessage("Movement detected.");
        }

        private static void Program_LoudNoise()
        {
            logMessage("Noise detected.");
        }

        private static void imageWriteTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            logMessage("Saving snapshot.");

            try
            {
                lock (_imageLocker)
                {
                    _image.Save(SnapshotFilename);
                }
            }
            catch (Exception ex)
            {
                logException(ex);
            }
        }

        private static void imageReadTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                lock (_cameraLocker)
                {
                    var image = _camera.ReadAs<Bgr<byte>>();
                    _detector.ProcessFrame(image.AsAForgeImage());
                    lock (_imageLocker)
                    {
                        _image = image.Clone();
                    }
                }
            }
            catch (Exception ex)
            {
                cleanupCamera();
                System.Threading.Thread.Sleep(5000);
                initCamera();
                logException(ex);
                return;
            }

            if (_detector.MotionLevel > 0.02)
            {
                Movement();
            }
        }

        private static void initCamera()
        {
            lock (_cameraLocker)
            {
                try
                {
                    _camera = new CameraCapture();
                }
                catch (Exception ex)
                {
                    logException(ex);
                }

                try
                {
                    _camera.Open();
                    _detector.ProcessFrame(_camera.ReadAs<Bgr<byte>>().AsAForgeImage());
                    System.Threading.Thread.Sleep(1000);
                    _detector.ProcessFrame(_camera.ReadAs<Bgr<byte>>().AsAForgeImage());
                    System.Threading.Thread.Sleep(1000);
                    _detector.ProcessFrame(_camera.ReadAs<Bgr<byte>>().AsAForgeImage());
                }
                catch (Exception ex)
                {
                    try
                    {
                        _camera.Dispose();
                    }
                    finally
                    {
                        _camera = null;
                    }
                    logException(ex);
                }
            }
        }

        private static void cleanupCamera()
        {
            lock (_cameraLocker)
            {
                try
                {
                    _camera.Dispose();
                }
                finally
                {
                    _camera = null;
                }
            }
        }

        private static void logTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (_doLogLocker)
            {
                if (!_doLog)
                {
                    return;
                }

                string curTimeString = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                logMessage(string.Format("Saving noise and snapshot to '{0}'.", curTimeString));

                try
                {
                    string folder = Path.Combine(FileRoot, curTimeString);
                    Directory.CreateDirectory(folder);

                    string noiseFilename = Path.Combine(folder, "noise.mp3");
                    using (var writer = new LameMP3FileWriter(noiseFilename, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1), LAMEPreset.STANDARD))
                    {
                        var noiseSamples = getLatestSamples();
                        writer.Write(noiseSamples, 0, noiseSamples.Length);
                    }

                    lock (_imageLocker)
                    {
                        _image.Save(Path.Combine(folder, "snapshot.jpg"));
                    }
                }
                catch (Exception ex)
                {
                    logException(ex);
                }
                finally
                {
                    _doLog = false;
                }
            }
        }

        private static void DoLog()
        {
            lock (_doLogLocker)
            {
                _doLog = true;
            }
        }

        private static void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            int nPotentialNoises;
            try
            {
                lock (_queueLocker)
                {
                    _queue.Dequeue();
                    var next = new float[e.BytesRecorded / sizeof(float)];
                    unsafe
                    {
                        fixed (float* pNext = next)
                        fixed (byte* pBuffer = e.Buffer)
                        {
                            memcpy(new IntPtr(pNext), new IntPtr(pBuffer), new UIntPtr((uint)e.BytesRecorded));
                        }
                    }
                    _queue.Enqueue(next);

                    var avgs = _queue.Take(8).Select(s => s.Select(v => v * v).Average()).ToList();
                    float avg = _queue.Take(8).SelectMany(s => s.Select(v => v * v)).Average();
                    nPotentialNoises = avgs.Count(a => a / avg > 2.5);
                }
            }
            catch (Exception ex)
            {
                logException(ex);
                return;
            }

            if (nPotentialNoises > _nHistoricalNoises)
            {
                LoudNoise();
                _nHistoricalNoises++;
            }
            else if (nPotentialNoises < _nHistoricalNoises)
            {
                _nHistoricalNoises--;
            }
        }

        private static byte[] getLatestSamples()
        {
            lock (_queueLocker)
            {
                return _queue.SelectMany(s => s.SelectMany(v => BitConverter.GetBytes(v))).ToArray();
            }
        }

        private static void logException(Exception ex)
        {
            for (int nTry = 0; nTry < 5; nTry++)
            {
                try
                {
                    lock (_logLocker)
                    {
                        using (var writer = new StreamWriter(_logStream, System.Text.Encoding.Default, 1024, true))
                        {
                            writer.WriteLine("{0} : {1}", DateTime.Now.ToString("yyyy-MM-dd, hh:mm:ss"), ex.Message);
                            writer.WriteLine(ex.StackTrace);
                        }

                        Console.WriteLine("{0} : {1}", DateTime.Now.ToString("yyyy-MM-dd, hh:mm:ss"), ex.Message);
                        Console.WriteLine(ex.StackTrace);
                    }
                }
                catch (Exception)
                {
                    System.Threading.Thread.Sleep(500);
                    continue;
                }

                break;
            }

            ExceptionLogged();
        }

        private static void logMessage(string message)
        {
            for (int nTry = 0; nTry < 5; nTry++)
            {
                try
                {
                    lock (_logLocker)
                    {
                        using (var writer = new StreamWriter(_logStream, System.Text.Encoding.Default, 1024, true))
                        {
                            writer.WriteLine("{0} : {1}", DateTime.Now.ToString("yyyy-MM-dd, hh:mm:ss"), message);
                        }

                        Console.WriteLine("{0} : {1}", DateTime.Now.ToString("yyyy-MM-dd, hh:mm:ss"), message);
                    }
                }
                catch (Exception)
                {
                    System.Threading.Thread.Sleep(500);
                    continue;
                }

                break;
            }
        }
    }
}
