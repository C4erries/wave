using System;
using System.IO;

namespace PS5000A
{
    class Program
    {
        static void Main(string[] args)
        {
            const int N = 4096;
            const int SR = 100000;
            const double FREQ = 1000.0;

            var samples = new float[N];
            for (int i = 0; i < N; i++)
                samples[i] = (float)Math.Sin(2.0 * Math.PI * FREQ * i / SR);

            byte[] frame = BrokerPublisher.EncodeFrame(
                timestampNs: 0L,
                sampleRateHz: SR,
                nSamples: N,
                channelId: 0,
                sourceId: 1,
                samples: samples
            );

            string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "csharp_frame.bin");
            File.WriteAllBytes(outPath, frame);
            Console.WriteLine("Written: " + outPath);
            Console.WriteLine("Size: " + frame.Length + " bytes (expected " + (20 + N * 4) + ")");

            // Print first 20 header bytes for manual inspection
            Console.Write("Header bytes: ");
            for (int i = 0; i < 20; i++)
                Console.Write(frame[i].ToString("X2") + " ");
            Console.WriteLine();

            // Print first 3 sample values (big-endian float32)
            Console.WriteLine("First sample bytes: " +
                frame[20].ToString("X2") + " " + frame[21].ToString("X2") + " " +
                frame[22].ToString("X2") + " " + frame[23].ToString("X2"));
        }
    }
}
